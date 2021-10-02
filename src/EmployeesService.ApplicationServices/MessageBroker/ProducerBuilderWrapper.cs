﻿using System;
using Confluent.Kafka;
using EmployeesService.ApplicationServices.Configurations;
using Microsoft.Extensions.Options;

namespace EmployeesService.ApplicationServices.MessageBroker
{
    public class ProducerBuilderWrapper : IProducerBuilderWrapper
    {
        /// <inheritdoc cref="Producer"/>
        public IProducer<string, string> Producer { get; set; }
        /// <inheritdoc cref="CreateNewEmployeeTopic"/>
        public string CreateNewEmployeeTopic { get; set; }
        /// <inheritdoc cref="MoveToConferenceTopic"/>
        public string MoveToConferenceTopic { get; set; }

        public ProducerBuilderWrapper(IOptions<KafkaConfiguration> configuration)
        {
            var configValue = configuration.Value;
            if (configValue is null)
                throw new Exception("Configuration for kafka server was not specified");

            var producerConfig = new ProducerConfig
            {
                BootstrapServers = configValue.BootstrapServers
            };

            Producer = new ProducerBuilder<string, string>(producerConfig).Build();
            CreateNewEmployeeTopic = configValue.CreateNewEmployeeTopic;
            MoveToConferenceTopic = configValue.MoveToConferenceTopic;
        }
    }
}
