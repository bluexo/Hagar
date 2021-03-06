using Hagar.Buffers;
using Hagar.Session;
using Hagar.WireProtocol;
using System;
using System.Buffers;
using System.Runtime.CompilerServices;

namespace Hagar.Codecs
{
    public static class ReferenceCodec
    {
        /// <summary>
        /// Indicates that the field being serialized or deserialized is a value type.
        /// </summary>
        /// <param name="session">The serializer session.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void MarkValueField(SerializerSession session) => session.ReferencedObjects.MarkValueField();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryWriteReferenceField<TBufferWriter>(
            ref Writer<TBufferWriter> writer,
            uint fieldId,
            Type expectedType,
            object value) where TBufferWriter : IBufferWriter<byte>
        {
            if (!writer.Session.ReferencedObjects.GetOrAddReference(value, out var reference))
            {
                return false;
            }

            writer.WriteFieldHeader(fieldId, expectedType, value?.GetType(), WireType.Reference);
            writer.WriteVarInt(reference);
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T ReadReference<T, TInput>(ref Reader<TInput> reader, Field field) => (T)ReadReference(ref reader, field, typeof(T));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static object ReadReference<TInput>(ref Reader<TInput> reader, Field field, Type expectedType)
        {
            var reference = reader.ReadVarUInt32();
            if (!reader.Session.ReferencedObjects.TryGetReferencedObject(reference, out var value))
            {
                ThrowReferenceNotFound(expectedType, reference);
            }

            return value switch
            {
                UnknownFieldMarker marker => DeserializeFromMarker(ref reader, field, marker, reference, expectedType),
                _ => value,
            };
        }

        private static object DeserializeFromMarker<TInput>(
            ref Reader<TInput> reader,
            Field field,
            UnknownFieldMarker marker,
            uint reference,
            Type lastResortFieldType)
        {
            // Capture state from the reader and session.
            var session = reader.Session;
            var originalPosition = reader.Position;
            var referencedObjects = session.ReferencedObjects;
            var originalCurrentReferenceId = referencedObjects.CurrentReferenceId;
            var originalReferenceToObjectCount = referencedObjects.ReferenceToObjectCount;

            // Deserialize the object, replacing the marker in the session.
            try
            {
                // Create a reader at the position specified by the marker.
                reader.ForkFrom(marker.Position, out var referencedReader);

                // Determine the correct type for the field.
                var fieldType = marker.Field.FieldType ?? field.FieldType ?? lastResortFieldType;

                // Get a serializer for that type.
                var specificSerializer = session.CodecProvider.GetCodec(fieldType);

                // Reset the session's reference id so that the deserialized objects overwrite the placeholder markers.
                referencedObjects.CurrentReferenceId = reference - 1;
                referencedObjects.ReferenceToObjectCount = referencedObjects.GetReferenceIndex(marker);
                return specificSerializer.ReadValue(ref referencedReader, marker.Field);
            }
            finally
            {
                // Revert the reference id.
                referencedObjects.CurrentReferenceId = originalCurrentReferenceId;
                referencedObjects.ReferenceToObjectCount = originalReferenceToObjectCount;
                reader.ResumeFrom(originalPosition);
            }
        }

        public static void RecordObject(SerializerSession session, object value) => session.ReferencedObjects.RecordReferenceField(value);
        public static void RecordObject(SerializerSession session, object value, uint referenceId) => session.ReferencedObjects.RecordReferenceField(value, referenceId);

        /// <summary>
        /// Records and returns a placeholder reference id for objects which cannot be immediately deserialized.
        /// </summary>
        public static uint CreateRecordPlaceholder(SerializerSession session)
        {
            var referencedObject = session.ReferencedObjects;
            return ++referencedObject.CurrentReferenceId;
        }

        private static void ThrowReferenceNotFound(Type expectedType, uint reference) => throw new ReferenceNotFoundException(expectedType, reference);
    }
}