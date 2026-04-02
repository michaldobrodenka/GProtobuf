using System.Collections.Generic;
using ProtoBuf;

namespace GProtobuf.Tests.TestModel
{
    /// <summary>
    /// Test model for [ProtoBeforeSerialization] and [ProtoAfterSerialization] callbacks.
    /// </summary>
    [ProtoContract]
    public class SerializationCallbackModel
    {
        [ProtoMember(1)]
        public string Name { get; set; }

        [ProtoMember(2)]
        public int Value { get; set; }

        [ProtoMember(3)]
        public string ComputedChecksum { get; set; }

        /// <summary>
        /// Transient field - tracks callback invocations for testing.
        /// </summary>
        public List<string> CallbackLog { get; set; } = new List<string>();

        /// <summary>
        /// Counter for BeforeSerialization calls.
        /// </summary>
        public int BeforeSerializationCount { get; set; }

        /// <summary>
        /// Counter for AfterSerialization calls.
        /// </summary>
        public int AfterSerializationCount { get; set; }

        [ProtoBeforeSerialization]
        internal void OnBeforeSerialization()
        {
            BeforeSerializationCount++;
            CallbackLog.Add("BeforeSerialization");

            // Compute checksum before serialization (demonstrates real use case)
            ComputedChecksum = $"HASH:{Name?.Length ?? 0}:{Value}";
        }

        [ProtoAfterSerialization]
        internal void OnAfterSerialization()
        {
            AfterSerializationCount++;
            CallbackLog.Add("AfterSerialization");
        }
    }

    /// <summary>
    /// Test model with multiple callbacks.
    /// </summary>
    [ProtoContract]
    public class MultipleCallbacksModel
    {
        [ProtoMember(1)]
        public string Data { get; set; }

        public List<string> CallbackLog { get; set; } = new List<string>();

        [ProtoBeforeSerialization]
        internal void PrepareData()
        {
            CallbackLog.Add("PrepareData");
        }

        [ProtoBeforeSerialization]
        internal void ValidateData()
        {
            CallbackLog.Add("ValidateData");
        }

        [ProtoAfterSerialization]
        internal void Cleanup()
        {
            CallbackLog.Add("Cleanup");
        }
    }

    /// <summary>
    /// Test model where callback modifies state that affects serialized size.
    /// This tests the two-pass approach correctness.
    /// </summary>
    [ProtoContract]
    public class TwoPassCallbackModel
    {
        [ProtoMember(1)]
        public byte[] Payload { get; set; }

        [ProtoMember(2)]
        public int PayloadSize { get; set; }

        public bool BeforeCallbackInvoked { get; set; }
        public bool AfterCallbackInvoked { get; set; }

        [ProtoBeforeSerialization]
        internal void ComputePayloadSize()
        {
            BeforeCallbackInvoked = true;
            // This modifies a field that will be serialized
            // In two-pass approach, this MUST be called BEFORE size calculation
            PayloadSize = Payload?.Length ?? 0;
        }

        [ProtoAfterSerialization]
        internal void OnAfter()
        {
            AfterCallbackInvoked = true;
        }
    }

    /// <summary>
    /// Struct with serialization callbacks.
    /// </summary>
    [ProtoContract]
    public struct CallbackStruct
    {
        [ProtoMember(1)]
        public int Value { get; set; }

        [ProtoMember(2)]
        public int DoubledValue { get; set; }

        // Note: Structs can have callbacks but they're more limited
        // since the instance is copied. We use ref return pattern.
        [ProtoBeforeSerialization]
        internal void OnBeforeSerialization()
        {
            DoubledValue = Value * 2;
        }
    }

    #region Inheritance with Callbacks

    /// <summary>
    /// Base class with serialization callbacks for inheritance testing.
    /// </summary>
    [ProtoContract]
    [ProtoInclude(100, typeof(DerivedWithCallback))]
    [ProtoInclude(101, typeof(DerivedWithoutCallback))]
    public class BaseWithCallback
    {
        [ProtoMember(1)]
        public string BaseName { get; set; }

        [ProtoMember(2)]
        public string BaseComputedValue { get; set; }

        public List<string> CallbackLog { get; set; } = new List<string>();

        [ProtoBeforeSerialization]
        internal void BaseOnBeforeSerialization()
        {
            CallbackLog.Add("Base.BeforeSerialization");
            BaseComputedValue = $"BASE:{BaseName?.Length ?? 0}";
        }

        [ProtoAfterSerialization]
        internal void BaseOnAfterSerialization()
        {
            CallbackLog.Add("Base.AfterSerialization");
        }
    }

    /// <summary>
    /// Derived class with its own callbacks.
    /// </summary>
    [ProtoContract]
    public class DerivedWithCallback : BaseWithCallback
    {
        [ProtoMember(1)]
        public string DerivedName { get; set; }

        [ProtoMember(2)]
        public string DerivedComputedValue { get; set; }

        [ProtoBeforeSerialization]
        internal void DerivedOnBeforeSerialization()
        {
            CallbackLog.Add("Derived.BeforeSerialization");
            DerivedComputedValue = $"DERIVED:{DerivedName?.Length ?? 0}";
        }

        [ProtoAfterSerialization]
        internal void DerivedOnAfterSerialization()
        {
            CallbackLog.Add("Derived.AfterSerialization");
        }
    }

    /// <summary>
    /// Derived class without its own callbacks (relies on base callbacks).
    /// </summary>
    [ProtoContract]
    public class DerivedWithoutCallback : BaseWithCallback
    {
        [ProtoMember(1)]
        public int DerivedValue { get; set; }
    }

    #endregion

    #region Nested Objects with Callbacks

    /// <summary>
    /// Parent object that contains a child with callbacks.
    /// Tests whether nested object callbacks are invoked.
    /// </summary>
    [ProtoContract]
    public class ParentWithNestedCallback
    {
        [ProtoMember(1)]
        public string ParentName { get; set; }

        [ProtoMember(2)]
        public NestedChildWithCallback Child { get; set; }

        [ProtoMember(3)]
        public int ParentValue { get; set; }
    }

    /// <summary>
    /// Child object with its own callbacks, used as nested member.
    /// </summary>
    [ProtoContract]
    public class NestedChildWithCallback
    {
        [ProtoMember(1)]
        public string ChildName { get; set; }

        [ProtoMember(2)]
        public string ChildComputedValue { get; set; }

        public bool BeforeCallbackInvoked { get; set; }
        public bool AfterCallbackInvoked { get; set; }

        [ProtoBeforeSerialization]
        internal void OnBeforeSerialization()
        {
            BeforeCallbackInvoked = true;
            ChildComputedValue = $"CHILD:{ChildName?.Length ?? 0}";
        }

        [ProtoAfterSerialization]
        internal void OnAfterSerialization()
        {
            AfterCallbackInvoked = true;
        }
    }

    #endregion

    #region Collections with Callback Objects

    /// <summary>
    /// Container with List of objects that have callbacks.
    /// </summary>
    [ProtoContract]
    public class ContainerWithCallbackList
    {
        [ProtoMember(1)]
        public string ContainerName { get; set; }

        [ProtoMember(2)]
        public List<ItemWithCallback> Items { get; set; }
    }

    /// <summary>
    /// Container with Dictionary where values have callbacks.
    /// </summary>
    [ProtoContract]
    public class ContainerWithCallbackDictionary
    {
        [ProtoMember(1)]
        public string ContainerName { get; set; }

        [ProtoMember(2)]
        public Dictionary<int, ItemWithCallback> ItemsById { get; set; }
    }

    /// <summary>
    /// Item used in collections, has its own callbacks.
    /// </summary>
    [ProtoContract]
    public class ItemWithCallback
    {
        [ProtoMember(1)]
        public int Id { get; set; }

        [ProtoMember(2)]
        public string Name { get; set; }

        [ProtoMember(3)]
        public string ComputedHash { get; set; }

        public bool BeforeCallbackInvoked { get; set; }
        public bool AfterCallbackInvoked { get; set; }

        [ProtoBeforeSerialization]
        internal void OnBeforeSerialization()
        {
            BeforeCallbackInvoked = true;
            ComputedHash = $"ITEM:{Id}:{Name?.Length ?? 0}";
        }

        [ProtoAfterSerialization]
        internal void OnAfterSerialization()
        {
            AfterCallbackInvoked = true;
        }
    }

    #endregion

    #region Exception Handling in Callbacks

    /// <summary>
    /// Model where BeforeSerialization callback throws an exception.
    /// Tests exception propagation.
    /// </summary>
    [ProtoContract]
    public class ExceptionInBeforeCallback
    {
        [ProtoMember(1)]
        public string Data { get; set; }

        public bool ShouldThrow { get; set; }
        public bool AfterCallbackInvoked { get; set; }

        [ProtoBeforeSerialization]
        internal void OnBeforeSerialization()
        {
            if (ShouldThrow)
            {
                throw new System.InvalidOperationException("BeforeSerialization failed!");
            }
        }

        [ProtoAfterSerialization]
        internal void OnAfterSerialization()
        {
            AfterCallbackInvoked = true;
        }
    }

    /// <summary>
    /// Model where AfterSerialization callback throws an exception.
    /// Tests that serialization completes before exception propagates.
    /// </summary>
    [ProtoContract]
    public class ExceptionInAfterCallback
    {
        [ProtoMember(1)]
        public string Data { get; set; }

        public bool ShouldThrow { get; set; }
        public bool BeforeCallbackInvoked { get; set; }

        [ProtoBeforeSerialization]
        internal void OnBeforeSerialization()
        {
            BeforeCallbackInvoked = true;
        }

        [ProtoAfterSerialization]
        internal void OnAfterSerialization()
        {
            if (ShouldThrow)
            {
                throw new System.InvalidOperationException("AfterSerialization failed!");
            }
        }
    }

    #endregion

    #region Static Callbacks

    /// <summary>
    /// Model with static callback methods.
    /// Static callbacks should NOT be invoked (no instance context).
    /// </summary>
    [ProtoContract]
    public class StaticCallbackModel
    {
        [ProtoMember(1)]
        public string Data { get; set; }

        /// <summary>
        /// Static counter to track if static callback was invoked.
        /// </summary>
        public static int StaticBeforeCount { get; set; }
        public static int StaticAfterCount { get; set; }

        public static void ResetCounters()
        {
            StaticBeforeCount = 0;
            StaticAfterCount = 0;
        }

        [ProtoBeforeSerialization]
        internal static void OnBeforeSerializationStatic()
        {
            StaticBeforeCount++;
        }

        [ProtoAfterSerialization]
        internal static void OnAfterSerializationStatic()
        {
            StaticAfterCount++;
        }
    }

    #endregion

    #region Callbacks with Parameters

    /// <summary>
    /// Model with callbacks that have parameters.
    /// protobuf-net supports StreamingContext parameter - we should skip these or handle them.
    /// </summary>
    [ProtoContract]
    public class ParameterizedCallbackModel
    {
        [ProtoMember(1)]
        public string Data { get; set; }

        public bool BeforeWithParamInvoked { get; set; }
        public bool BeforeWithoutParamInvoked { get; set; }

        /// <summary>
        /// Callback with parameter - should be skipped by generator.
        /// </summary>
        [ProtoBeforeSerialization]
        internal void OnBeforeWithParam(System.Runtime.Serialization.StreamingContext context)
        {
            BeforeWithParamInvoked = true;
        }

        /// <summary>
        /// Callback without parameter - should be invoked.
        /// </summary>
        [ProtoBeforeSerialization]
        internal void OnBeforeWithoutParam()
        {
            BeforeWithoutParamInvoked = true;
        }
    }

    #endregion

    #region Deep Inheritance (3+ levels)

    /// <summary>
    /// Root of 3-level inheritance hierarchy.
    /// </summary>
    [ProtoContract]
    [ProtoInclude(100, typeof(MiddleWithCallback))]
    public class RootWithCallback
    {
        [ProtoMember(1)]
        public string RootName { get; set; }

        public List<string> CallbackLog { get; set; } = new List<string>();

        [ProtoBeforeSerialization]
        internal void RootOnBefore()
        {
            CallbackLog.Add("Root.Before");
        }

        [ProtoAfterSerialization]
        internal void RootOnAfter()
        {
            CallbackLog.Add("Root.After");
        }
    }

    /// <summary>
    /// Middle level of 3-level inheritance.
    /// </summary>
    [ProtoContract]
    [ProtoInclude(100, typeof(LeafWithCallback))]
    public class MiddleWithCallback : RootWithCallback
    {
        [ProtoMember(1)]
        public string MiddleName { get; set; }

        [ProtoBeforeSerialization]
        internal void MiddleOnBefore()
        {
            CallbackLog.Add("Middle.Before");
        }

        [ProtoAfterSerialization]
        internal void MiddleOnAfter()
        {
            CallbackLog.Add("Middle.After");
        }
    }

    /// <summary>
    /// Leaf level of 3-level inheritance.
    /// </summary>
    [ProtoContract]
    public class LeafWithCallback : MiddleWithCallback
    {
        [ProtoMember(1)]
        public string LeafName { get; set; }

        [ProtoBeforeSerialization]
        internal void LeafOnBefore()
        {
            CallbackLog.Add("Leaf.Before");
        }

        [ProtoAfterSerialization]
        internal void LeafOnAfter()
        {
            CallbackLog.Add("Leaf.After");
        }
    }

    #endregion

    #region Private Callbacks

    /// <summary>
    /// Model with private callback methods.
    /// Private callbacks may or may not be detected by the generator.
    /// </summary>
    [ProtoContract]
    public class PrivateCallbackModel
    {
        [ProtoMember(1)]
        public string Data { get; set; }

        [ProtoMember(2)]
        public string ComputedValue { get; set; }

        public bool PrivateBeforeInvoked { get; set; }
        public bool PrivateAfterInvoked { get; set; }

        [ProtoBeforeSerialization]
        private void OnBeforePrivate()
        {
            PrivateBeforeInvoked = true;
            ComputedValue = $"PRIVATE:{Data?.Length ?? 0}";
        }

        [ProtoAfterSerialization]
        private void OnAfterPrivate()
        {
            PrivateAfterInvoked = true;
        }
    }

    #endregion

    #region Array of Callback Objects

    /// <summary>
    /// Container with array (not List) of callback objects.
    /// </summary>
    [ProtoContract]
    public class ContainerWithCallbackArray
    {
        [ProtoMember(1)]
        public string ContainerName { get; set; }

        [ProtoMember(2)]
        public ItemWithCallback[] Items { get; set; }
    }

    #endregion

    #region Serialization Counter Model

    /// <summary>
    /// Model that counts how many times callbacks are invoked.
    /// Used to test multiple serialization calls.
    /// </summary>
    [ProtoContract]
    public class CallbackCounterModel
    {
        [ProtoMember(1)]
        public string Data { get; set; }

        public int BeforeCount { get; set; }
        public int AfterCount { get; set; }

        [ProtoBeforeSerialization]
        internal void OnBefore()
        {
            BeforeCount++;
        }

        [ProtoAfterSerialization]
        internal void OnAfter()
        {
            AfterCount++;
        }
    }

    #endregion

    #region Deserialization Callbacks

    /// <summary>
    /// Test model for [ProtoBeforeDeserialization] and [ProtoAfterDeserialization] callbacks.
    /// </summary>
    [ProtoContract]
    public class DeserializationCallbackModel
    {
        [ProtoMember(1)]
        public string Name { get; set; }

        [ProtoMember(2)]
        public int Value { get; set; }

        public List<string> CallbackLog { get; set; } = new List<string>();

        public int BeforeDeserializationCount { get; set; }
        public int AfterDeserializationCount { get; set; }

        [ProtoBeforeDeserialization]
        internal void OnBeforeDeserialization()
        {
            BeforeDeserializationCount++;
            CallbackLog.Add("BeforeDeserialization");
        }

        [ProtoAfterDeserialization]
        internal void OnAfterDeserialization()
        {
            AfterDeserializationCount++;
            CallbackLog.Add("AfterDeserialization");
        }
    }

    /// <summary>
    /// Test model with all four callback types.
    /// </summary>
    [ProtoContract]
    public class FullCallbackModel
    {
        [ProtoMember(1)]
        public string Name { get; set; }

        [ProtoMember(2)]
        public int Value { get; set; }

        public List<string> CallbackLog { get; set; } = new List<string>();

        [ProtoBeforeSerialization]
        internal void OnBeforeSerialization()
        {
            CallbackLog.Add("BeforeSerialization");
        }

        [ProtoAfterSerialization]
        internal void OnAfterSerialization()
        {
            CallbackLog.Add("AfterSerialization");
        }

        [ProtoBeforeDeserialization]
        internal void OnBeforeDeserialization()
        {
            CallbackLog.Add("BeforeDeserialization");
        }

        [ProtoAfterDeserialization]
        internal void OnAfterDeserialization()
        {
            CallbackLog.Add("AfterDeserialization");
        }
    }

    /// <summary>
    /// Base class with deserialization callbacks for inheritance testing.
    /// </summary>
    [ProtoContract]
    [ProtoInclude(100, typeof(DerivedWithDeserializationCallback))]
    public class BaseWithDeserializationCallback
    {
        [ProtoMember(1)]
        public string BaseName { get; set; }

        public List<string> CallbackLog { get; set; } = new List<string>();

        [ProtoBeforeDeserialization]
        internal void BaseOnBeforeDeserialization()
        {
            CallbackLog.Add("Base.BeforeDeserialization");
        }

        [ProtoAfterDeserialization]
        internal void BaseOnAfterDeserialization()
        {
            CallbackLog.Add("Base.AfterDeserialization");
        }
    }

    /// <summary>
    /// Derived class with its own deserialization callbacks.
    /// </summary>
    [ProtoContract]
    public class DerivedWithDeserializationCallback : BaseWithDeserializationCallback
    {
        [ProtoMember(1)]
        public int DerivedValue { get; set; }

        [ProtoBeforeDeserialization]
        internal void DerivedOnBeforeDeserialization()
        {
            CallbackLog.Add("Derived.BeforeDeserialization");
        }

        [ProtoAfterDeserialization]
        internal void DerivedOnAfterDeserialization()
        {
            CallbackLog.Add("Derived.AfterDeserialization");
        }
    }

    #endregion
}
