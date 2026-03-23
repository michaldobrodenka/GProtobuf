using System;

namespace GProtobuf.Core
{
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public class GenerateSerializerAttribute : Attribute
    {
        public Type CollectionType { get; }

        public GenerateSerializerAttribute(Type collectionType)
        {
            CollectionType = collectionType;
        }
    }
}
