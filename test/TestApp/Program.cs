using System;
using System.IO.Pipelines;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using Hagar.Serializers;
using Hagar.Session;
using Hagar;
using Hagar.Buffers;
using Hagar.Codecs;
using Hagar.Configuration;
using Hagar.Invocation;
using Hagar.ISerializable;
using Hagar.Json;
using Microsoft.Extensions.DependencyInjection;
using MyPocos;
using Newtonsoft.Json;
using NodaTime;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace TestApp
{
    [GenerateMethodSerializers(typeof(MyProxyBaseClass))]
    public interface IGrain { }

    [GenerateMethodSerializers(typeof(MyProxyBaseClass), isExtension: true)]
    public interface IGrainExtension { }

    public class Program
    {
        public static async Task TestRpc()
        {
            Console.WriteLine("Hello World!");
            var serviceProvider = new ServiceCollection()
                .AddHagar(builder => builder.AddAssembly(typeof(SomeClassWithSerialzers).Assembly))
                .BuildServiceProvider();

            var config = serviceProvider.GetRequiredService<IConfiguration<SerializerConfiguration>>();
            var allProxies = config.Value.InterfaceProxies;

            var proxy = GetProxy<IMyInvokable>();
            await proxy.Multiply(4, 5, "hello");
            var proxyBase = proxy as MyProxyBaseClass;
            using var invocation = proxyBase.Invocations.First();
            invocation.SetTarget(new TargetHolder(new MyImplementation(), serviceProvider));
            await invocation.Invoke();

            var generic = GetProxy<IMyInvokable<int>>();
            //((MyProxyBaseClass)generic).Invocations.Find()
            await generic.DoStuff<string>();

            TInterface GetProxy<TInterface>()
            {
                if (typeof(TInterface).IsGenericType)
                {
                    var unbound = typeof(TInterface).GetGenericTypeDefinition();
                    var parameters = typeof(TInterface).GetGenericArguments();
                    foreach (var proxyType in allProxies)
                    {
                        if (!proxyType.IsGenericType) continue;
                        var matching = proxyType.FindInterfaces(
                            (type, criteria) => type.IsGenericType && type.GetGenericTypeDefinition() == (Type)criteria,
                            unbound).FirstOrDefault();
                        if (matching != null)
                        {
                            return (TInterface)Activator.CreateInstance(proxyType.GetGenericTypeDefinition().MakeGenericType(parameters));
                        }
                    }
                }

                return (TInterface)Activator.CreateInstance(
                    allProxies.First(p => typeof(TInterface).IsAssignableFrom(p)));
            }
        }

        public class ComponentCollection<TKey>
        {
            private readonly Dictionary<TKey, object> components = new Dictionary<TKey, object>();

            public T GetComponent<T>(TKey key)
            {
                this.components.TryGetValue(key, out var component);
                return (T)component;
            }
        }

        public interface IKeyedExtension<TKey>
        {
            TKey Key { get; }
        }

        internal struct TargetHolder : ITargetHolder
        {
            private List<object> components;
            private Dictionary<string, object> stringComponents;
            private readonly object target;
            private readonly IServiceProvider services;

            public TargetHolder(object target, IServiceProvider services)
            {
                this.target = target;
                this.services = services;
                this.stringComponents = new Dictionary<string, object>();
                this.components = new List<object>();
            }

            public T GetComponent<T>()
            {
                {
                    if (this.target is T typed) return typed;
                }

                components ??= new List<object>();
                foreach (var component in components)
                {
                    if (component is T typed) return typed;
                }

                {
                    // Create and add the component.
                    var typed = services.GetRequiredService<T>();
                    components.Add(typed);
                    return typed;
                }
            }

            public T GetComponent<TKey, T>(TKey key)
            {
                if (typeof(TKey) == typeof(string))
                {
                    var stringKey = key as string;
                    if (stringComponents.TryGetValue(stringKey, out var result))
                    {
                        return (T)result;
                    }
                    else
                    {
                        var typedResult = services.GetRequiredService<T>();
                        this.stringComponents[stringKey] = typedResult;
                        return typedResult;
                    }
                }
                else
                {
                    {
                        if (this.target is T typed && typed is IKeyedExtension<TKey> keyed && keyed.Key.Equals(key)) return typed;
                    }

                    components ??= new List<object>();
                    foreach (var component in components)
                    {
                        if (component is T typed && typed is IKeyedExtension<TKey> keyed && keyed.Key.Equals(key)) return typed;
                    }

                    {
                        // Create and add the component.
                        var typed = services.GetRequiredService<T>();
                        if (!(typed is IKeyedExtension<TKey> keyed && keyed.Key.Equals(key))) ThrowNotKeyed<T, TKey>(typed);
                        components.Add(typed);
                        return typed;
                    }

                    throw new NotSupportedException($"Components with key type {typeof(TKey)} are not supported on this instance");
                }
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void ThrowNotKeyed<T, TKey>(T component) => throw new InvalidOperationException($"Component {component} of type {component.GetType()} does not implement {typeof(IKeyedExtension<TKey>)}");
        }

        internal class MyImplementation : IMyInvokable
        {
            public ValueTask<int> Multiply(int a, int b, object c) => new ValueTask<int>(a * b);
        }
        internal class MyImplementation<T> : IMyInvokable<T>
        {
            public Task DoStuff<TU>() => Task.CompletedTask;
        }

        public static void TestOne()
        {
            Console.WriteLine("Hello World!");
            var serviceProvider = new ServiceCollection()
                .AddHagar(builder => builder.AddAssembly(typeof(SomeClassWithSerialzers).Assembly))                
                .BuildServiceProvider();

            var codecs = serviceProvider.GetRequiredService<ITypedCodecProvider>();

            var codec = codecs.GetCodec<SomeClassWithSerialzers>();
            var sessionPool = serviceProvider.GetRequiredService<SessionPool>();

            var writeSession = sessionPool.GetSession();
            var pipe = new Pipe();
            var writer = new Writer<PipeWriter>(pipe.Writer, writeSession);
            codec.WriteField(ref writer,
                             0,
                             typeof(SomeClassWithSerialzers),
                             new SomeClassWithSerialzers { IntField = 2, IntProperty = 30 });
            writer.Commit();
            pipe.Writer.FlushAsync().GetAwaiter().GetResult();
            pipe.Writer.Complete();
            pipe.Reader.TryRead(out var readResult);

            using (var readerSession = sessionPool.GetSession())
            {
                var reader = new Reader(readResult.Buffer, readerSession);
                var initialHeader = reader.ReadFieldHeader();
                var result = codec.ReadValue(ref reader, initialHeader);
                Console.WriteLine(result);
            }
        }

        public static void Main(string[] args)
        {
            var serviceProvider = new ServiceCollection()
                .AddHagar(builder => builder.AddAssembly(typeof(SomeClassWithSerialzers).Assembly))
                .BuildServiceProvider();

            SerializerSession GetSession() => serviceProvider.GetRequiredService<SessionPool>().GetSession();
            TestRpc().GetAwaiter().GetResult();
            //return;
            TestOne();
            
            Test(
                GetSession,
                new AbstractTypeSerializer<object>(),
                new WhackyJsonType
                {
                    Number = 7,
                    String = "bananas!"
                });
            var mySerializable = new MySerializableClass
            {
                String = "yolooo",
                Integer = 38,
                Self = null,
            };
            Test(
                GetSession,
                new AbstractTypeSerializer<object>(),
                mySerializable
            );

            mySerializable.Self = mySerializable;
            Test(
                GetSession,
                new AbstractTypeSerializer<object>(),
                mySerializable
            );

            Exception exception = null;
            try
            {
                throw new ReferenceNotFoundException(typeof(int), 2401);
            }
            catch (Exception e)
            {
                exception = e;
            }

            Test(
                GetSession,
                new AbstractTypeSerializer<object>(),
                exception
            );

            Test(GetSession, new AbstractTypeSerializer<object>(), new LocalDate());

            Console.WriteLine("Press any key to exit.");
            Console.ReadKey();
        }

        private static void Test<T>(Func<SerializerSession> getSession, IFieldCodec<T> serializer, T expected)
        {
            using var writerSession = getSession();
            var pipe = new Pipe();
            var writer = new Writer<PipeWriter>(pipe.Writer, writerSession);

            serializer.WriteField(ref writer, 0, typeof(T), expected);
            writer.Commit();

            Console.WriteLine($"Size: {writer.Position} bytes.");
            Console.WriteLine($"Wrote References:\n{GetWriteReferenceTable(writerSession)}");

            pipe.Writer.FlushAsync().GetAwaiter().GetResult();
            pipe.Writer.Complete();
            pipe.Reader.TryRead(out var readResult);
            using var readerSesssion = getSession();
            var reader = new Reader(readResult.Buffer, readerSesssion);
            var initialHeader = reader.ReadFieldHeader();

            Console.WriteLine("Header:");
            Console.WriteLine(initialHeader.ToString());

            var actual = serializer.ReadValue(ref reader, initialHeader);
            pipe.Reader.AdvanceTo(readResult.Buffer.End);
            pipe.Reader.Complete();

            Console.WriteLine($"Expect: {expected}\nActual: {actual}");

            var references = GetReadReferenceTable(reader.Session);
            Console.WriteLine($"Read references:\n{references}");
        }

        private static StringBuilder GetReadReferenceTable(SerializerSession session)
        {
            var table = session.ReferencedObjects.CopyReferenceTable();
            var references = new StringBuilder();
            foreach (var entry in table)
            {
                references.AppendLine($"\t[{entry.Key}] {entry.Value}");
            }
            return references;
        }

        private static StringBuilder GetWriteReferenceTable(SerializerSession session)
        {
            var table = session.ReferencedObjects.CopyIdTable();
            var references = new StringBuilder();
            foreach (var entry in table)
            {
                references.AppendLine($"\t[{entry.Value}] {entry.Key}");
            }
            return references;
        }

        [Serializable]
        public class MySerializableClass : ISerializable
        {
            public string String { get; set; }
            public int Integer { get; set; }
            public MySerializableClass Self { get; set; }

            public MySerializableClass()
            {
            }

            protected MySerializableClass(SerializationInfo info, StreamingContext context)
            {
                this.String = info.GetString(nameof(this.String));
                this.Integer = info.GetInt32(nameof(this.Integer));
                this.Self = (MySerializableClass) info.GetValue(nameof(this.Self), typeof(MySerializableClass));
            }

            public void GetObjectData(SerializationInfo info, StreamingContext context)
            {
                info.AddValue(nameof(this.String), this.String);
                info.AddValue(nameof(this.Integer), this.Integer);
                info.AddValue(nameof(this.Self), this.Self);
            }

            public override string ToString()
            {
                string refString = this.Self == this ? "[this]" : $"[{this.Self?.ToString() ?? "null"}]";
                return $"{base.ToString()}, {nameof(this.String)}: {this.String}, {nameof(this.Integer)}: {this.Integer}, Self: {refString}";
            }
        }
    }

    public class WhackyJsonType
    {
        public int Number { get; set; }
        public string String { get; set; }

        public override string ToString() => JsonConvert.SerializeObject(this);
    }


}