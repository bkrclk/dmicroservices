﻿using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DMicroservices.RabbitMq.Base;
using DMicroservices.RabbitMq.Model;
using DMicroservices.Utils.Logger;
using Newtonsoft.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace DMicroservices.RabbitMq.Consumer
{
    /// <summary>
    /// Consuming base
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public abstract class BasicConsumer<T> : IConsumer
    {
        public abstract string ListenQueueName { get; }

        public abstract bool AutoAck { get; }

        public virtual ushort PrefectCount { get; set; }

        public virtual byte MaxPriority { get; set; } = 0;

        public virtual ExchangeContent ExchangeContent { get; set; }

        public virtual Action<T, BasicDeliverEventArgs> DataReceivedAction { get; }

        private bool _consumerListening = false;

        public bool ConsumerListening
        {
            get => _consumerListening;
            set
            {
                _consumerListening = value;
            }
        }

        /// <summary>
        /// Modeli dinlemek için kullanıclan event
        /// </summary>
        private EventingBasicConsumer _eventingBasicConsumer;

        /// <summary>
        /// bu consumer tekrar initialize edilebilir mi?
        /// </summary>
        private bool _dontReinitialize = false;

        private IModel _rabbitMqChannel;

        private readonly object _stateChangeLockObject = new object();


        protected BasicConsumer()
        {

        }


        private void RabbitMqChannelShutdown()
        {
            try
            {
                _eventingBasicConsumer.OnCancel(_eventingBasicConsumer.ConsumerTags);
                _rabbitMqChannel?.Dispose();
                _rabbitMqChannel = null;
            }
            catch
            {
                //ignored
            }

            Thread.Sleep(3000);

            ConsumerListening = false;
            if (_dontReinitialize)
                return;
            
            StartConsume();

        }

        private void DocumentConsumerOnReceived(object sender, BasicDeliverEventArgs e)
        {
            var jsonData = Encoding.UTF8.GetString(e.Body.ToArray());
            try
            {
                var parsedData = JsonConvert.DeserializeObject<T>(jsonData);
                DataReceivedAction(parsedData, e);
            }
            catch (Exception ex)
            {
                ElasticLogger.Instance.Error(ex, $"DocumentConsumer generic data received exception: {ex.Message}",
                    jsonData);
                _rabbitMqChannel.BasicNack(e.DeliveryTag, false, false);
            }
        }

        protected void BasicAck(ulong deliveryTag, bool multiple)
        {
            _rabbitMqChannel.BasicAck(deliveryTag, multiple);
        }

        protected EventingBasicConsumer GetCurrentConsumer()
        {
            return _eventingBasicConsumer;
        }

        public Task StartConsume()
        {
            return Task.Run(() =>
            {
                Debug.WriteLine($"Consumer {ListenQueueName} start requested.");
                lock (_stateChangeLockObject)
                {
                    Debug.WriteLine($"Consumer {ListenQueueName} start process started.");
                    if (ConsumerListening)
                        return;

                    try
                    {
                        if (string.IsNullOrEmpty(ListenQueueName))
                        {
                            ElasticLogger.Instance.Info("Consumer QueueName was null");
                        }

                        if (ExchangeContent != null)
                        {
                            if (ExchangeContent.RoutingKey == null ||
                                string.IsNullOrEmpty(ExchangeContent.ExchangeName) ||
                                string.IsNullOrEmpty(ExchangeContent.ExchangeType))
                                throw new Exception("ExchangeContent contains null object(s)!");
                            _rabbitMqChannel =
                                RabbitMqConnection.Instance.GetExchangeChannel(ExchangeContent, ListenQueueName);
                        }
                        else
                        {
                            _rabbitMqChannel = MaxPriority > 0
                                ? RabbitMqConnection.Instance.GetChannel(ListenQueueName, MaxPriority)
                                : RabbitMqConnection.Instance.GetChannel(ListenQueueName);
                        }

                        if (PrefectCount != 0)
                            _rabbitMqChannel.BasicQos(0, PrefectCount, false);

                        _eventingBasicConsumer = new EventingBasicConsumer(_rabbitMqChannel);
                        _eventingBasicConsumer.Received += DocumentConsumerOnReceived;
                        _rabbitMqChannel.BasicConsume(ListenQueueName, AutoAck, _eventingBasicConsumer);
                        ConsumerListening = true;
                        _rabbitMqChannel.ModelShutdown += (sender, args) =>
                        {
                            if (args.ReplyCode != 200)
                            {
                                ElasticLogger.Instance.Error(new Exception($"{args} Queue: {ListenQueueName}"), "RabbitMQ/ModelShutdown");
                                Task.Run(RabbitMqChannelShutdown);
                            }
                        };
                    }
                    catch (Exception ex)
                    {
                        ElasticLogger.Instance.Error(ex, "RabbitMQ/RabbitmqConsumer");
                    }
                }
                Debug.WriteLine($"Consumer {ListenQueueName} start completed.");
            });
        }

        public Task StopConsume()
        {
            return Task.Run(() =>
             {
                 Debug.WriteLine($"Consumer {ListenQueueName} stop requested.");
                 lock (_stateChangeLockObject)
                 {
                     Debug.WriteLine($"Consumer {ListenQueueName} stop process started.");
                     if (!ConsumerListening)
                         return;

                     _dontReinitialize = true;

                     _eventingBasicConsumer.Received -= DocumentConsumerOnReceived;
                     Thread.Sleep(TimeSpan.FromSeconds(15));
                     _eventingBasicConsumer.OnCancel(_eventingBasicConsumer.ConsumerTags); 
                     _rabbitMqChannel?.Dispose();
                     _rabbitMqChannel = null;
                     ConsumerListening = false;
                     Debug.WriteLine($"Consumer {ListenQueueName} stop completed.");
                 }
             });
        }

    }
}
