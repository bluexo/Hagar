﻿using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Hagar.Activator;
using Hagar.Buffers;
using Hagar.Codec;
using Hagar.Configuration;
using Hagar.Serializer;
using Hagar.Session;
using Hagar.TypeSystem;
using Hagar.WireProtocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hagar
{
    public static class ServiceProviderExtensions
    {
        public static IServiceCollection AddCryoBuf(this IServiceCollection services, Action<SerializerConfiguration> configure = null)
        {
            services.AddSingleton<IConfigurationProvider<SerializerConfiguration>, DefaultSerializerConfiguration>();
            services.AddSingleton<IConfigurationProvider<TypeConfiguration>, DefaultTypeConfiguration>();
            services.TryAddSingleton(typeof(IActivator<>), typeof(DefaultActivator<>));
            services.TryAddSingleton(typeof(IConfiguration<>), typeof(ConfigurationHolder<>));
            services.TryAddSingleton<ITypeResolver, CachedTypeResolver>();
            services.TryAddSingleton<CodecProvider>();
            services.TryAddSingleton<IUntypedCodecProvider>(sp => sp.GetRequiredService<CodecProvider>());
            services.TryAddSingleton<ITypedCodecProvider>(sp => sp.GetRequiredService<CodecProvider>());
            services.TryAddSingleton<IPartialSerializerProvider>(sp => sp.GetRequiredService<CodecProvider>());
            services.TryAddScoped(typeof(IFieldCodec<>), typeof(FieldCodecHolder<>));
            services.TryAddScoped(typeof(IPartialSerializer<>), typeof(PartialSerializerHolder<>));
            services.TryAddSingleton<WellKnownTypeCollection>();
            services.TryAddSingleton<TypeCodec>();

            // Session
            services.AddTransient<ReferencedTypeCollection>();
            services.AddTransient<ReferencedObjectCollection>();
            services.AddTransient<SerializerSession>();

            return services.ConfigureCryoBuf(configure);
        }

        public static IServiceCollection AddCryoBufSerializers(this IServiceCollection services, Assembly asm)
        {
            var attrs = asm.GetCustomAttributes<MetadataProviderAttribute>();
            foreach (var attr in attrs)
            {
                if (!typeof(IConfigurationProvider<SerializerConfiguration>).IsAssignableFrom(attr.ProviderType)) continue;
                services.AddSingleton(typeof(IConfigurationProvider<SerializerConfiguration>), attr.ProviderType);
            }

            return services;
        }

        public static IServiceCollection ConfigureCryoBuf<TOptions>(this IServiceCollection services, Action<TOptions> configure)
        {
            if (configure != null)
            {
                services.AddSingleton<IConfigurationProvider<TOptions>>(new DelegateConfigurationProvider<TOptions>(configure));
            }

            return services;
        }

        private class DelegateConfigurationProvider<TOptions> : IConfigurationProvider<TOptions>
        {
            private readonly Action<TOptions> configure;

            public DelegateConfigurationProvider(Action<TOptions> configure)
            {
                this.configure = configure;
            }

            public void Configure(TOptions configuration) => this.configure(configuration);
        }

        private class FieldCodecHolder<TField> : IFieldCodec<TField>
        {
            private readonly IFieldCodec<TField> codec;

            public FieldCodecHolder(ITypedCodecProvider codecProvider)
            {
                this.codec = codecProvider.GetCodec<TField>();
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void WriteField(Writer writer, SerializerSession session, uint fieldIdDelta, Type expectedType, TField value) => this.codec.WriteField(writer, session, fieldIdDelta, expectedType, value);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public TField ReadValue(Reader reader, SerializerSession session, Field field) => this.codec.ReadValue(reader, session, field);
        }

        private class PartialSerializerHolder<TField> : IPartialSerializer<TField> where TField : class
        {
            private readonly IPartialSerializer<TField> partialSerializer;
            public PartialSerializerHolder(IPartialSerializerProvider provider)
            {
                this.partialSerializer = provider.GetPartialSerializer<TField>();
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Serialize(Writer writer, SerializerSession session, TField value)
            {
                this.partialSerializer.Serialize(writer, session, value);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Deserialize(Reader reader, SerializerSession session, TField value)
            {
                this.partialSerializer.Deserialize(reader, session, value);
            }
        }
    }
}
