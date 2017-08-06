﻿using System;
using Hagar.Session;
using Hagar.Utilities;
using Hagar.WireProtocol;

namespace Hagar.Codec
{
    public class IntegerCodec : IFieldCodec<byte>, IFieldCodec<sbyte>, IFieldCodec<int>, IFieldCodec<uint>,
        IFieldCodec<short>, IFieldCodec<ushort>, IFieldCodec<long>, IFieldCodec<ulong>, IFieldCodec<char>
    {
        void IFieldCodec<char>.WriteField(
            Writer writer,
            SerializerSession session,
            uint fieldId,
            Type expectedType,
            char value)
        {
            ReferenceCodec.MarkValueField(session);
            writer.WriteFieldHeader(session, fieldId, expectedType, typeof(char), WireType.VarInt);
            writer.WriteVarInt(value);
        }

        char IFieldCodec<char>.ReadValue(Reader reader, SerializerSession session, Field field)
        {
            ReferenceCodec.MarkValueField(session);
            return (char) reader.ReadUInt8(field.WireType);
        }

        void IFieldCodec<byte>.WriteField(
            Writer writer,
            SerializerSession session,
            uint fieldId,
            Type expectedType,
            byte value)
        {
            ReferenceCodec.MarkValueField(session);
            writer.WriteFieldHeader(session, fieldId, expectedType, typeof(byte), WireType.VarInt);
            writer.WriteVarInt(value);
        }

        byte IFieldCodec<byte>.ReadValue(Reader reader, SerializerSession session, Field field)
        {
            ReferenceCodec.MarkValueField(session);
            return reader.ReadUInt8(field.WireType);
        }

        void IFieldCodec<sbyte>.WriteField(
            Writer writer,
            SerializerSession session,
            uint fieldId,
            Type expectedType,
            sbyte value)
        {
            ReferenceCodec.MarkValueField(session);
            writer.WriteFieldHeader(session, fieldId, expectedType, typeof(sbyte), WireType.VarInt);
            writer.WriteVarInt(value);
        }

        sbyte IFieldCodec<sbyte>.ReadValue(Reader reader, SerializerSession session, Field field)
        {
            ReferenceCodec.MarkValueField(session);
            return reader.ReadInt8(field.WireType);
        }

        void IFieldCodec<short>.WriteField(
            Writer writer,
            SerializerSession session,
            uint fieldId,
            Type expectedType,
            short value)
        {
            ReferenceCodec.MarkValueField(session);
            writer.WriteFieldHeader(session, fieldId, expectedType, typeof(short), WireType.VarInt);
            writer.WriteVarInt(value);
        }

        ushort IFieldCodec<ushort>.ReadValue(Reader reader, SerializerSession session, Field field)
        {
            ReferenceCodec.MarkValueField(session);
            return reader.ReadUInt16(field.WireType);
        }

        void IFieldCodec<ushort>.WriteField(
            Writer writer,
            SerializerSession session,
            uint fieldId,
            Type expectedType,
            ushort value)
        {
            ReferenceCodec.MarkValueField(session);
            writer.WriteFieldHeader(session, fieldId, expectedType, typeof(ushort), WireType.VarInt);
            writer.WriteVarInt(value);
        }

        short IFieldCodec<short>.ReadValue(Reader reader, SerializerSession session, Field field)
        {
            ReferenceCodec.MarkValueField(session);
            return reader.ReadInt16(field.WireType);
        }

        void IFieldCodec<uint>.WriteField(
            Writer writer,
            SerializerSession session,
            uint fieldId,
            Type expectedType,
            uint value)
        {
            ReferenceCodec.MarkValueField(session);
            if (value > 1 << 20)
            {
                writer.WriteFieldHeader(session, fieldId, expectedType, typeof(uint), WireType.Fixed32);
                writer.Write(value);
            }
            else
            {
                writer.WriteFieldHeader(session, fieldId, expectedType, typeof(uint), WireType.VarInt);
                writer.WriteVarInt(value);
            }
        }

        uint IFieldCodec<uint>.ReadValue(Reader reader, SerializerSession session, Field field)
        {
            ReferenceCodec.MarkValueField(session);
            return reader.ReadUInt32(field.WireType);
        }

        void IFieldCodec<int>.WriteField(
            Writer writer,
            SerializerSession session,
            uint fieldId,
            Type expectedType,
            int value)
        {
            ReferenceCodec.MarkValueField(session);
            if (value > 1 << 20 || -value > 1 << 20)
            {
                writer.WriteFieldHeader(session, fieldId, expectedType, typeof(int), WireType.Fixed32);
                writer.Write(value);
            }
            else
            {
                writer.WriteFieldHeader(session, fieldId, expectedType, typeof(int), WireType.VarInt);
                writer.WriteVarInt(value);
            }
        }

        int IFieldCodec<int>.ReadValue(Reader reader, SerializerSession session, Field field)
        {
            ReferenceCodec.MarkValueField(session);
            return reader.ReadInt32(field.WireType);
        }

        void IFieldCodec<long>.WriteField(Writer writer, SerializerSession session, uint fieldId, Type expectedType, long value)
        {
            ReferenceCodec.MarkValueField(session);
            if (value <= int.MaxValue && value >= int.MinValue)
            {
                if (value > 1 << 20 || -value > 1 << 20)
                {
                    writer.WriteFieldHeader(session, fieldId, expectedType, typeof(long), WireType.Fixed32);
                    writer.Write(value);
                }
                else
                {
                    writer.WriteFieldHeader(session, fieldId, expectedType, typeof(long), WireType.VarInt);
                    writer.WriteVarInt(value);
                }
            }
            else if (value > 1 << 41 || -value > 1 << 41)
            {
                writer.WriteFieldHeader(session, fieldId, expectedType, typeof(long), WireType.Fixed64);
                writer.Write(value);
            }
            else
            {
                writer.WriteFieldHeader(session, fieldId, expectedType, typeof(long), WireType.VarInt);
                writer.WriteVarInt(value);
            }
        }

        long IFieldCodec<long>.ReadValue(Reader reader, SerializerSession session, Field field)
        {
            ReferenceCodec.MarkValueField(session);
            return reader.ReadInt64(field.WireType);
        }

        void IFieldCodec<ulong>.WriteField(
            Writer writer,
            SerializerSession session,
            uint fieldId,
            Type expectedType,
            ulong value)
        {
            ReferenceCodec.MarkValueField(session);
            if (value <= int.MaxValue)
            {
                if (value > 1 << 20)
                {
                    writer.WriteFieldHeader(session, fieldId, expectedType, typeof(ulong), WireType.Fixed32);
                    writer.Write(value);
                }
                else
                {
                    writer.WriteFieldHeader(session, fieldId, expectedType, typeof(ulong), WireType.VarInt);
                    writer.WriteVarInt(value);
                }
            }
            else if (value > 1 << 41)
            {
                writer.WriteFieldHeader(session, fieldId, expectedType, typeof(ulong), WireType.Fixed64);
                writer.Write(value);
            }
            else
            {
                writer.WriteFieldHeader(session, fieldId, expectedType, typeof(ulong), WireType.VarInt);
                writer.WriteVarInt(value);
            }
        }

        ulong IFieldCodec<ulong>.ReadValue(Reader reader, SerializerSession session, Field field)
        {
            ReferenceCodec.MarkValueField(session);
            return reader.ReadUInt64(field.WireType);
        }
    }
}