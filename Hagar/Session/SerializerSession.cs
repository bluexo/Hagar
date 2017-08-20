using System.Runtime.Serialization;
using Hagar.TypeSystem;

namespace Hagar.Session
{
    public class SerializerSession
    {
        public TypeCodec TypeCodec { get; } = new TypeCodec();
        public WellKnownTypeCollection WellKnownTypes { get; } = new WellKnownTypeCollection();
        public ReferencedTypeCollection ReferencedTypes { get; } = new ReferencedTypeCollection();
        public ReferencedObjectCollection ReferencedObjects { get; } = new ReferencedObjectCollection();
        public StreamingContext StreamingContext { get; set; }

        public void Reset()
        {
            this.ReferencedObjects.Reset();
        }
    }
}