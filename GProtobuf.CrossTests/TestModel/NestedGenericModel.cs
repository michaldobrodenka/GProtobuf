using ProtoBuf;
using System;
using System.Collections.Generic;

namespace GProtobuf.CrossTests.TestModel
{
    /// <summary>
    /// Simple custom type for testing nested generics.
    /// </summary>
    [ProtoContract]
    public class DeviceActionType
    {
        [ProtoMember(1)]
        public int ActionId { get; set; }

        [ProtoMember(2)]
        public string ActionName { get; set; }

        [ProtoMember(3)]
        public double ActionValue { get; set; }

        public override bool Equals(object obj)
        {
            if (obj is DeviceActionType other)
            {
                return ActionId == other.ActionId &&
                       ActionName == other.ActionName &&
                       Math.Abs(ActionValue - other.ActionValue) < 0.0001;
            }
            return false;
        }

        public override int GetHashCode() => HashCode.Combine(ActionId, ActionName, ActionValue);
    }

    /// <summary>
    /// Custom type for dictionary key testing.
    /// </summary>
    [ProtoContract]
    public class DeviceValueType
    {
        [ProtoMember(1)]
        public int TypeId { get; set; }

        [ProtoMember(2)]
        public string TypeName { get; set; }

        public override bool Equals(object obj)
        {
            if (obj is DeviceValueType other)
            {
                return TypeId == other.TypeId && TypeName == other.TypeName;
            }
            return false;
        }

        public override int GetHashCode() => HashCode.Combine(TypeId, TypeName);
    }

    /// <summary>
    /// Custom type for array value testing.
    /// </summary>
    [ProtoContract]
    public class DeviceAggregation
    {
        [ProtoMember(1)]
        public int AggId { get; set; }

        [ProtoMember(2)]
        public string AggName { get; set; }

        public override bool Equals(object obj)
        {
            if (obj is DeviceAggregation other)
            {
                return AggId == other.AggId && AggName == other.AggName;
            }
            return false;
        }

        public override int GetHashCode() => HashCode.Combine(AggId, AggName);
    }

    /// <summary>
    /// Custom type for standalone array testing.
    /// </summary>
    [ProtoContract]
    public class GenericCcu
    {
        [ProtoMember(1)]
        public int CcuId { get; set; }

        [ProtoMember(2)]
        public string CcuName { get; set; }

        public override bool Equals(object obj)
        {
            if (obj is GenericCcu other)
            {
                return CcuId == other.CcuId && CcuName == other.CcuName;
            }
            return false;
        }

        public override int GetHashCode() => HashCode.Combine(CcuId, CcuName);
    }
}
