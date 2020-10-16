﻿using FASTER.core;
using Hagar;
using Hagar.Configuration;
using Hagar.Invocation;
using HagarGeneratedCode.CallLog;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace CallLog
{
    class Program
    {
        static async Task Main(string[] args) => await Host
            .CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddHagar(hagar =>
                {
                    hagar
                        .AddAssembly(typeof(Program).Assembly)
                        .AddISerializableSupport();
                });
                services.AddSingleton<ApplicationContext>();
                services.AddSingleton<Catalog>();
                services.AddSingleton<MessageRouter>();
                services.AddSingleton<LogManager>();
                services.AddSingleton<LogEnumerator>();
                services.AddSingleton<ProxyFactory>();
                services.AddSingleton<IHostedService, MyApp>();
                services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<LogManager>());
                services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<LogEnumerator>());
            })
            .RunConsoleAsync();
    }

    internal class MyApp : BackgroundService
    {
        private readonly ILogger<MyApp> _log;
        private readonly Catalog _catalog;
        private readonly ProxyFactory _proxyFactory;
        private readonly IServiceProvider _serviceProvider;
        private readonly ApplicationContext _context;

        public MyApp(ILogger<MyApp> log, Catalog catalog, ProxyFactory proxyFactory, IServiceProvider serviceProvider, ApplicationContext context)
        {
            _log = log;
            _catalog = catalog;
            _proxyFactory = proxyFactory;
            _serviceProvider = serviceProvider;
            _context = context;
            _catalog.RegisterGrain(context.Id, context);
        }

        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            var id = IdSpan.Create("counter1");
            var grain1 = ActivatorUtilities.CreateInstance<WorkflowContext>(_serviceProvider, id);
            grain1.Instance = ActivatorUtilities.CreateInstance<CounterWorkflow>(_serviceProvider, id);
            _catalog.RegisterGrain(id, grain1);

            id = IdSpan.Create("counter2");
            var grain2 = ActivatorUtilities.CreateInstance<WorkflowContext>(_serviceProvider, id);
            grain2.Instance = ActivatorUtilities.CreateInstance<CounterWorkflow>(_serviceProvider, id);
            _catalog.RegisterGrain(id, grain2);

            await grain1.ActivateAsync();
            await grain2.ActivateAsync();

            await base.StartAsync(cancellationToken);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var id = IdSpan.Create("counter1");
            var proxy = _proxyFactory.GetProxy<ICounterWorkflow, WorkflowProxyBase>(id);
            RuntimeContext.Current = _context;
            while (!stoppingToken.IsCancellationRequested)
            {
                //var result = await proxy.PingPongFriend(IdSpan.Create("counter2"), 1);
                //_log.LogInformation("Got result: {DateTime}", result);
                await Task.Delay(10_000);
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            await base.StopAsync(cancellationToken);
        }
    }

    public static class RuntimeContext
    {
        private static readonly AsyncLocal<IWorkflowContext> _runtimeContext = new AsyncLocal<IWorkflowContext>();

        public static IWorkflowContext Current { get => _runtimeContext.Value; set => _runtimeContext.Value = value; }
    }

    [GenerateSerializer]
    public class Increment
    {
    }

    [GenerateMethodSerializers(typeof(WorkflowProxyBase))]
    public interface IWorkflow
    {
    }

    public interface ICounterWorkflow : IWorkflow
    {
        ValueTask Increment();
        ValueTask<DateTime> PingPongFriend(IdSpan friend, int cycles);
    }

    internal struct RequestState
    {
        public Response Response { get; set; }
        public IResponseCompletionSource Completion { get; set; }
    }


    [GenerateSerializer]
    internal class ActivationMarker : Request
    {
        /// <summary>
        /// The time of this activation.
        /// </summary>
        [Id(1)]
        public DateTime Time { get; set; }

        /// <summary>
        /// The unique identifier for this activation.
        /// </summary>
        [Id(2)]
        public Guid InvocationId { get; set; }

        /// <summary>
        /// The version of the grain at the time of this activation.
        /// </summary>
        [Id(3)]
        public int Version { get; set; }

        public override int ArgumentCount => 0;

        public override void Dispose()
        {
        }

        public override TArgument GetArgument<TArgument>(int index) => default;
        public override TTarget GetTarget<TTarget>() => default;
        public override void SetArgument<TArgument>(int index, in TArgument value) { }
        public override void SetTarget<TTargetHolder>(TTargetHolder holder) { }
        protected override ValueTask InvokeInner()
        {
            ((WorkflowContext)RuntimeContext.Current).OnActivationMarker(this);
            return default;
        }
    }

    public class WorkflowEnvironment
    {
        public static ValueTask<DateTime> GetUtcNow() => Promise.Record(() => DateTime.UtcNow);
    }

    public class Promise
    {
        public static ValueTask<T> Record<T>(Func<T> func)
        {
            var completion = ResponseCompletionSourcePool.Get<T>();
            var current = RuntimeContext.Current;
            if (current.PrepareRequest(completion, out var sequenceNumber))
            {
                current.OnMessage(new Message { SenderId = current.Id, SequenceNumber = sequenceNumber, Body = Response.FromResult<T>(func()), });
            }    

            return completion.AsValueTask();
        }
    }

    [GenerateSerializer]
    public class Message
    {
        [Id(1)]
        public IdSpan SenderId { get; set; }

        [Id(2)]
        public long SequenceNumber { get; set; }

        [Id(3)]
        public object Body { get; set; }
    }

    [GenerateSerializer]
    public class LogEntry
    {
        [Id(1)]
        public IdSpan WorkflowId { get; set; }

        [Id(2)]
        public object Payload { get; set; }
    }

    public interface IWorkflowContext
    {
        IdSpan Id { get; }

        ValueTask ActivateAsync();

        void OnMessage(object message);

        bool PrepareRequest(IResponseCompletionSource completion, out long sequenceNumber);

        ValueTask DeactivateAsync();
    }

    internal class Catalog 
    {
        private readonly ConcurrentDictionary<IdSpan, IWorkflowContext> _grains = new ConcurrentDictionary<IdSpan, IWorkflowContext>(IdSpan.Comparer.Instance);

        public void RegisterGrain(IdSpan grainId, IWorkflowContext grain)
        {
            _grains[grainId] = grain;
        }

        public IWorkflowContext GetGrain(IdSpan grainId)
        {
            if (_grains.TryGetValue(grainId, out var grain))
            {
                return grain;
            }

            throw new InvalidOperationException();
        }
    }

    internal class MessageRouter
    {
        private readonly Catalog _catalog;

        public MessageRouter(Catalog catalog) => _catalog = catalog;

        public void SendMessage(IdSpan grainId, Message message)
        {
            _catalog.GetGrain(grainId).OnMessage(message);
        }
    }

    internal class LogManager : BackgroundService, IDisposable 
    {
        private readonly FasterLog _dbLog;
        private readonly Serializer<LogEntry> _logEntrySerializer;

        public LogManager(Serializer<LogEntry> logEntrySerializer)
        {
            var path = Path.GetTempPath() + "Orleansia\\";
            IDevice device = Devices.CreateLogDevice(path + "main.log");

            // FasterLog will recover and resume if there is a previous commit found
            _dbLog = new FasterLog(new FasterLogSettings { LogDevice = device });
            _logEntrySerializer = logEntrySerializer;
        }

        public long EnqueueLogEntry(IdSpan grainId, object payload)
        {
            var bytes = _logEntrySerializer.SerializeToArray(new LogEntry
            {
                WorkflowId = grainId,
                Payload = payload,
            }, sizeHint: 20);

            return _dbLog.Enqueue(bytes);
        }

        public async ValueTask EnqueueLogEntryAndWaitForCommitAsync(IdSpan grainId, object payload)
        {
            var bytes = _logEntrySerializer.SerializeToArray(new LogEntry
            {
                WorkflowId = grainId,
                Payload = payload,
            }, sizeHint: 20);

            await _dbLog.EnqueueAndWaitForCommitAsync(bytes);
        }

        public ValueTask WaitForCommitAsync(long untilAddress, CancellationToken cancellationToken) => _dbLog.WaitForCommitAsync(untilAddress, cancellationToken);

        public FasterLog Log => _dbLog;

        public override void Dispose()
        {
            base.Dispose();
            _dbLog.Dispose();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(1_000, stoppingToken);
                    await _dbLog.CommitAsync(stoppingToken);
                }
                catch
                {

                }
            }
        }
    }

    internal class LogEnumerator : IHostedService
    {
        private readonly ILogger<LogEnumerator> _log;
        private readonly Channel<LogEntryHolder> _entryChannel;
        private readonly ChannelReader<LogEntryHolder> _entryChannelReader;
        private readonly ChannelWriter<LogEntryHolder> _entryChannelWriter;
        private readonly ConcurrentDictionary<IdSpan, RecoveryState> _recoveryChannels = new ConcurrentDictionary<IdSpan, RecoveryState>();
        private readonly Serializer<LogEntry> _entrySerializer;
        private readonly FasterLog _dbLog;
        private readonly Catalog _catalog;
        private readonly CancellationTokenSource _shutdownCancellation = new CancellationTokenSource();
        private Task _runTask;

        public LogEnumerator(LogManager logManager, Catalog catalog, Serializer<LogEntry> entrySerializer, ILogger<LogEnumerator> log)
        {
            _entryChannel = Channel.CreateUnbounded<LogEntryHolder>(new UnboundedChannelOptions
            {
                SingleWriter = true,
                SingleReader = true,
            });
            _entryChannelReader = _entryChannel.Reader;
            _entryChannelWriter = _entryChannel.Writer;
            _dbLog = logManager.Log;
            _catalog = catalog;
            _entrySerializer = entrySerializer;
            _log = log;
        }

        private class RecoveryState
        {
            public Channel<object> Entries { get; } = Channel.CreateUnbounded<object>(new UnboundedChannelOptions
            {
                SingleWriter = true,
                SingleReader = true,
            });
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _runTask = Task.WhenAll(Task.Run(RunLogReader), Task.Run(RunReader));
            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _shutdownCancellation.Cancel();
            await _runTask;
        }

        public async Task RunLogReader()
        {
            try
            {
                using var iterator = _dbLog.Scan(beginAddress: _dbLog.BeginAddress, endAddress: long.MaxValue, name: "main", recover: true);
                await foreach (var (entry, entryLength, currentAddress, nextAddress) in iterator.GetAsyncEnumerable(_shutdownCancellation.Token))
                {
                    if (!_entryChannelWriter.TryWrite(new LogEntryHolder(entry, entryLength, currentAddress, nextAddress)))
                    {
                        break;
                    }

                    //iterator.CompleteUntil(currentAddress);
                }
            }
            catch (Exception exception)
            {
                _log.LogError(exception, "Error reading log");
            }
        }

        public Channel<object> GetCommittedLogEntries(IdSpan id) => _recoveryChannels.GetOrAdd(id, _ => new RecoveryState()).Entries;

        public async Task RunReader()
        {
            try
            {
                while (await _entryChannelReader.WaitToReadAsync(_shutdownCancellation.Token))
                {
                    while (_entryChannelReader.TryRead(out var holder))
                    {
                        var entry = _entrySerializer.Deserialize(holder.Payload);
                        var grain = _catalog.GetGrain(entry.WorkflowId);
                        var state = _recoveryChannels.GetOrAdd(entry.WorkflowId, _ => new RecoveryState());
                        state.Entries.Writer.TryWrite(entry.Payload);
                    }
                }
            }
            catch (Exception exception)
            {
                _log.LogError(exception, "Error reading log");
            }
        }

        private readonly struct LogEntryHolder
        {
            private readonly byte[] _payload;
            private readonly int _length;

            public LogEntryHolder(byte[] payload, int length, long address, long nextAddress)
            {
                _payload = payload;
                _length = length;
                Address = address;
                NextAddress = nextAddress;
            }

            public Span<byte> Payload => _payload.AsSpan(0, _length);

            public long Address { get; }
            public long NextAddress { get; }
        }
    }

    // request => log => enumerator => target
    // response => log => enumerator => caller

    internal class ApplicationContext : IWorkflowContext
    {
        private long _nextRequestId = 0;
        private ILogger<ApplicationContext> _log;
        private readonly ConcurrentDictionary<long, IResponseCompletionSource> _pendingRequests = new ConcurrentDictionary<long, IResponseCompletionSource>();

        public ApplicationContext(ILogger<ApplicationContext> logger)
        {
            _log = logger;
        }

        public IdSpan Id { get; } = IdSpan.Create("app");
        public ValueTask ActivateAsync() => default;
        public ValueTask DeactivateAsync() => default;
        public void OnReplayMessage(object message)
        {
            _log.LogInformation("Replaying {Message}", message);
        }

        public void OnMessage(object message)
        {
            if (message is Message msg && msg.Body is Response response)
            {
                if (_pendingRequests.TryRemove(msg.SequenceNumber, out var completion))
                {
                    completion.Complete(response);
                }
                else
                {
                    _log.LogWarning("No pending request matching response for sequence {SequenceNumber}", msg.SequenceNumber);
                }
            }
            else
            {
                _log.LogWarning("Unsupported message of type {Type}: {Message}", message?.GetType(), message);
            }
        }

        public bool PrepareRequest(IResponseCompletionSource completion, out long sequenceNumber)
        {
            var requestId = Interlocked.Increment(ref _nextRequestId);
            sequenceNumber = requestId;
            _pendingRequests[requestId] = completion;

            return true;
        }
    }

    internal abstract class WorkflowProxyBase
    {
        private readonly IdSpan _id;
        private readonly MessageRouter _router;

        protected WorkflowProxyBase(IdSpan id, MessageRouter router)
        {
            _id = id;
            _router = router;
        }

        protected void SendRequest(IResponseCompletionSource callback, IInvokable body)
        {
            var caller = RuntimeContext.Current;

            if (caller is null)
            {
                throw new InvalidOperationException("No RuntimeContext set. Set a runtime context before making calls");
            }

            if (caller.PrepareRequest(callback, out var sequenceNumber))
            {
                // Send the message only if the sender signals that it should be sent.
                var callerId = caller.Id;
                _router.SendMessage(
                    _id,
                    new Message
                    {
                        SenderId = callerId,
                        SequenceNumber = sequenceNumber,
                        Body = body
                    });
            }
        }
    }
    
    public sealed class ProxyFactory
    {
        private readonly IServiceProvider _services;
        private readonly HashSet<Type> _knownProxies;
        private readonly ConcurrentDictionary<(Type, Type), Type> _proxyMap = new ConcurrentDictionary<(Type, Type), Type>();

        public ProxyFactory(IConfiguration<SerializerConfiguration> configuration, IServiceProvider services)
        {
            _services = services;
            _knownProxies = new HashSet<Type>(configuration.Value.InterfaceProxies);
        }

        private Type GetProxyType(Type interfaceType, Type baseType)
        {
            if (interfaceType.IsGenericType)
            {
                var unbound = interfaceType.GetGenericTypeDefinition();
                var parameters = interfaceType.GetGenericArguments();
                foreach (var proxyType in _knownProxies)
                {
                    if (!proxyType.IsGenericType)
                    {
                        continue;
                    }

                    if (!HasBaseType(proxyType.BaseType, baseType))
                    {
                        continue;
                    }

                    var matching = proxyType.FindInterfaces(
                            (type, criteria) =>
                                type.IsGenericType && type.GetGenericTypeDefinition() == (Type)criteria,
                            unbound)
                        .FirstOrDefault();
                    if (matching != null)
                    {
                        return proxyType.GetGenericTypeDefinition().MakeGenericType(parameters);
                    }
                }
            }

            return _knownProxies.First(interfaceType.IsAssignableFrom);

            static bool HasBaseType(Type type, Type baseType) => type switch
            {
                null => false,
                Type when type == baseType => true,
                _ => HasBaseType(type.BaseType, baseType)
            };
        }

        public TInterface GetProxy<TInterface, TBase>(IdSpan id)
        {
            if (!_proxyMap.TryGetValue((typeof(TInterface), typeof(TBase)), out var proxyType))
            {
                proxyType = _proxyMap[(typeof(TInterface), typeof(TBase))] = GetProxyType(typeof(TInterface), typeof(TBase));
            }

            return (TInterface)ActivatorUtilities.CreateInstance(_services, proxyType, id);
        }
    }

}
