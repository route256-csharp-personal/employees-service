﻿using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Confluent.Kafka;
using CSharpCourse.Core.Lib.Enums;
using CSharpCourse.Core.Lib.Events;
using CSharpCourse.EmployeesService.ApplicationServices.Exceptions;
using CSharpCourse.EmployeesService.ApplicationServices.MessageBroker;
using CSharpCourse.EmployeesService.ApplicationServices.Models.Commands;
using CSharpCourse.EmployeesService.ApplicationServices.Models.Enums;
using CSharpCourse.EmployeesService.Domain.AggregationModels;
using CSharpCourse.EmployeesService.Domain.AggregationModels.Conference;
using CSharpCourse.EmployeesService.Domain.AggregationModels.Employee;
using CSharpCourse.EmployeesService.Domain.Contracts;
using CSharpCourse.EmployeesService.Domain.Models;
using MediatR;

namespace CSharpCourse.EmployeesService.ApplicationServices.Handlers.Employees
{
    public class SendEmployeeToConferenceCommandHandler : IRequestHandler<SendEmployeeToConferenceCommand>
    {
        private readonly IConferenceRepository _conferenceRepository;
        private readonly IEmployeeRepository _employeeRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IMapper _mapper;
        private readonly IProducerBuilderWrapper _producerBuilderWrapper;

        public SendEmployeeToConferenceCommandHandler(IConferenceRepository conferenceRepository,
            IEmployeeRepository employeeRepository,
            IMapper mapper,
            IProducerBuilderWrapper producerBuilderWrapper,
            IUnitOfWork unitOfWork)
        {
            _conferenceRepository = conferenceRepository;
            _employeeRepository = employeeRepository;
            _mapper = mapper;
            _producerBuilderWrapper = producerBuilderWrapper;
            _unitOfWork = unitOfWork;
        }

        public async Task<Unit> Handle(SendEmployeeToConferenceCommand request, CancellationToken cancellationToken)
        {
            // Проверить что сотрудник существует
            var emp = await _employeeRepository.GetByIdWithIncludesAsync(request.EmployeeId, cancellationToken);
            if (emp is null)
                throw new BusinessException($"Employee with id {request.EmployeeId} not found in store");

            // Проверить что конференция еще не прошла
            var conf = await _conferenceRepository.CheckIsConferenceIsNotEndAsync(request.ConferenceId,
                cancellationToken);
            if (conf is null)
                throw new BusinessException($"Conference with id {request.ConferenceId} not found or is end");

            // Проверить что данный сотрудник еще не был на этой конференции
            if (emp.EmployeeConferences.Select(it => it.Conference.Id).Contains(request.EmployeeId))
                throw new BusinessException($"Employee with id {request.EmployeeId} was registered in " +
                                            $"conference with id {request.ConferenceId}");

            await _unitOfWork.StartTransaction(cancellationToken);

            // Записать что сотрудник идет на коференцию
            emp.EmployeeConferences.Add(new EmployeeConference()
                { ConferenceId = conf.Id, EmployeeId = request.EmployeeId });
            await _employeeRepository.UpdateAsync(emp, cancellationToken);

            var random = new Random();
            var randomManager = Constants
                .ManagersIssuingMerchandise[random.Next(0, Constants.ManagersIssuingMerchandise.Length)];

            // Отправить запрос на выдачу мерча
            await _producerBuilderWrapper.Producer.ProduceAsync(_producerBuilderWrapper.MoveToConferenceTopic,
                new Message<string, string>()
                {
                    Key = emp.Id.ToString(),
                    Value = JsonSerializer.Serialize(new NotificationEvent()
                    {
                        EmployeeEmail = emp.Email,
                        EmployeeName = $"{emp.LastName} {emp.FirstName} {emp.MiddleName}",
                        ManagerEmail = randomManager.ManagerEmail,
                        ManagerName = randomManager.ManagerName,
                        EventType = EmployeeEventType.ConferenceAttendance,
                        Payload = new MerchDeliveryEventPayload()
                        {
                            MerchType = request.AsWhom switch
                            {
                                (int)EmployeeInConferenceType.AsListener => MerchType.ConferenceListenerPack,
                                (int)EmployeeInConferenceType.AsSpeaker => MerchType.ConferenceSpeakerPack,
                                _ => throw new Exception("Merch type not defined")
                            },
                            ClothingSize = (Core.Lib.Enums.ClothingSize)emp.ClothingSize
                        }
                    })
                }, cancellationToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Unit.Value;
        }
    }
}
