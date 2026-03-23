using ProtoBuf;
using System.Collections.Generic;

namespace GProtobuf.CrossTests.TestModel
{
    /// <summary>
    /// Base class for value type conversions.
    /// This tests the fix for ProtoInclude wrapper size calculation.
    /// Bug: SizeCalculatorGenerator used ContentSize (all fields) for wrapper, but StreamWriter used OwnFieldsSize (own fields only).
    /// Fix: Use OwnFieldsSize in SizeCalculatorGenerator for ProtoInclude wrapper calculation.
    /// </summary>
    [ProtoContract]
    [ProtoInclude(100, typeof(LinearInterpolationConversion))]
    [ProtoInclude(101, typeof(AnalogSwitchConversion))]
    [ProtoInclude(102, typeof(MultiValueConversion))]
    public class ConversionBase
    {
        [ProtoMember(1)]
        public List<int> SourceTypes { get; set; }

        [ProtoMember(2)]
        public int TargetType { get; set; }
    }

    /// <summary>
    /// Derived class with own fields (double values).
    /// Similar to ValueTypeLinearInterpolationFunctionConversion.
    /// This tests that derived fields are correctly sized in ProtoInclude wrapper.
    /// </summary>
    [ProtoContract]
    public class LinearInterpolationConversion : ConversionBase
    {
        [ProtoMember(3)]
        public double Value1OnInput { get; set; }

        [ProtoMember(4)]
        public double Value1OnOutput { get; set; }

        [ProtoMember(5)]
        public double Value2OnInput { get; set; }

        [ProtoMember(6)]
        public double Value2OnOutput { get; set; }
    }

    /// <summary>
    /// Another derived class with different own fields.
    /// </summary>
    [ProtoContract]
    public class AnalogSwitchConversion : ConversionBase
    {
        [ProtoMember(3)]
        public double Threshold { get; set; }

        [ProtoMember(4)]
        public bool InvertLogic { get; set; }
    }

    /// <summary>
    /// Derived class with collection own field.
    /// </summary>
    [ProtoContract]
    public class MultiValueConversion : ConversionBase
    {
        [ProtoMember(3)]
        public List<double> Multipliers { get; set; }

        [ProtoMember(4)]
        public string Formula { get; set; }
    }

    /// <summary>
    /// Model that contains ProtoInclude hierarchy.
    /// Tests ProtoInclude wrapper size calculation fix.
    /// </summary>
    [ProtoContract]
    public class ProtoIncludeDerivedFieldsModel
    {
        [ProtoMember(1)]
        public int Id { get; set; }

        [ProtoMember(2)]
        public string Name { get; set; }

        /// <summary>
        /// List of conversions (polymorphic field).
        /// Tests ProtoInclude wrapper serialization/deserialization.
        /// </summary>
        [ProtoMember(3)]
        public List<ConversionBase> Conversions { get; set; }

        /// <summary>
        /// Single conversion (polymorphic field).
        /// Tests ProtoInclude wrapper for single field.
        /// </summary>
        [ProtoMember(4)]
        public ConversionBase PrimaryConversion { get; set; }
    }

    /// <summary>
    /// Model combining readonly struct keys with ProtoInclude hierarchy.
    /// This is the most complex case - similar to LightDevice with ValueTypeConversions.
    /// </summary>
    [ProtoContract]
    public class CombinedComplexModel
    {
        [ProtoMember(1)]
        public int Id { get; set; }

        /// <summary>
        /// Dictionary with readonly struct key and polymorphic value.
        /// </summary>
        [ProtoMember(2)]
        public Dictionary<ValueTypeKey, ConversionBase> ConversionsByType { get; set; }

        /// <summary>
        /// List of polymorphic items.
        /// </summary>
        [ProtoMember(3)]
        public List<ConversionBase> AllConversions { get; set; }

        /// <summary>
        /// Dictionary with readonly struct keys.
        /// </summary>
        [ProtoMember(4)]
        public Dictionary<ValueTypeKey, string> TypeNames { get; set; }
    }
}
