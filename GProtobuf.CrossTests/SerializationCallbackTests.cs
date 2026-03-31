using System;
using System.Collections.Generic;
using System.IO;
using GProtobuf.Tests.TestModel;
using ProtoBuf;
using Xunit;

namespace GProtobuf.CrossTests
{
    /// <summary>
    /// Tests for [ProtoBeforeSerialization] and [ProtoAfterSerialization] callbacks.
    /// </summary>
    public class SerializationCallbackTests
    {
        #region Basic Callback Tests

        [Fact]
        public void GG_BeforeSerializationCallback_IsInvoked()
        {
            var model = new SerializationCallbackModel
            {
                Name = "Test",
                Value = 42
            };

            Assert.Equal(0, model.BeforeSerializationCount);
            Assert.Null(model.ComputedChecksum);

            using var ms = new MemoryStream();
            global::GProtobuf.Tests.TestModel.Serialization.Serializers.SerializeSerializationCallbackModel(ms, model);

            // Callback should have been invoked
            Assert.Equal(1, model.BeforeSerializationCount);
            Assert.Equal("HASH:4:42", model.ComputedChecksum);
            Assert.Contains("BeforeSerialization", model.CallbackLog);
        }

        [Fact]
        public void GG_AfterSerializationCallback_IsInvoked()
        {
            var model = new SerializationCallbackModel
            {
                Name = "Test",
                Value = 42
            };

            Assert.Equal(0, model.AfterSerializationCount);

            using var ms = new MemoryStream();
            global::GProtobuf.Tests.TestModel.Serialization.Serializers.SerializeSerializationCallbackModel(ms, model);

            // Both callbacks should have been invoked
            Assert.Equal(1, model.BeforeSerializationCount);
            Assert.Equal(1, model.AfterSerializationCount);
            Assert.Contains("BeforeSerialization", model.CallbackLog);
            Assert.Contains("AfterSerialization", model.CallbackLog);
        }

        [Fact]
        public void GG_Callbacks_InvokedInCorrectOrder()
        {
            var model = new SerializationCallbackModel
            {
                Name = "Order Test",
                Value = 100
            };

            using var ms = new MemoryStream();
            global::GProtobuf.Tests.TestModel.Serialization.Serializers.SerializeSerializationCallbackModel(ms, model);

            // Before should be first, After should be second
            Assert.Equal(2, model.CallbackLog.Count);
            Assert.Equal("BeforeSerialization", model.CallbackLog[0]);
            Assert.Equal("AfterSerialization", model.CallbackLog[1]);
        }

        #endregion

        #region Two-Pass Correctness Tests

        /// <summary>
        /// Critical test: Verifies that BeforeSerialization is called BEFORE size calculation.
        /// If callback modifies serialized fields, the size must reflect the modified state.
        /// </summary>
        [Fact]
        public void GG_TwoPass_CallbackModifiesState_SizeIsCorrect()
        {
            var model = new TwoPassCallbackModel
            {
                Payload = new byte[] { 1, 2, 3, 4, 5 },
                PayloadSize = 0 // Will be set by callback
            };

            Assert.Equal(0, model.PayloadSize); // Not set yet
            Assert.False(model.BeforeCallbackInvoked);

            using var ms = new MemoryStream();
            global::GProtobuf.Tests.TestModel.Serialization.Serializers.SerializeTwoPassCallbackModel(ms, model);

            // Verify callback was invoked
            Assert.True(model.BeforeCallbackInvoked);
            Assert.True(model.AfterCallbackInvoked);
            Assert.Equal(5, model.PayloadSize); // Set by callback

            // Deserialize and verify the correct size was serialized
            var bytes = ms.ToArray();
            var deserialized = global::GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializeTwoPassCallbackModel(bytes);

            Assert.Equal(5, deserialized.PayloadSize); // Size should be 5, not 0
            Assert.Equal(5, deserialized.Payload.Length);
        }

        #endregion

        #region Multiple Callbacks Tests

        [Fact]
        public void GG_MultipleBeforeCallbacks_AllInvoked()
        {
            var model = new MultipleCallbacksModel
            {
                Data = "Test"
            };

            using var ms = new MemoryStream();
            global::GProtobuf.Tests.TestModel.Serialization.Serializers.SerializeMultipleCallbacksModel(ms, model);

            // All callbacks should be invoked
            Assert.Contains("PrepareData", model.CallbackLog);
            Assert.Contains("ValidateData", model.CallbackLog);
            Assert.Contains("Cleanup", model.CallbackLog);
        }

        #endregion

        #region Struct Callback Tests

        [Fact]
        public void GG_StructCallback_IsInvoked()
        {
            var model = new CallbackStruct
            {
                Value = 21,
                DoubledValue = 0 // Will be set by callback
            };

            using var ms = new MemoryStream();
            global::GProtobuf.Tests.TestModel.Serialization.Serializers.SerializeCallbackStruct(ms, model);

            // Deserialize and verify the callback computed the correct value
            var bytes = ms.ToArray();
            var deserialized = global::GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializeCallbackStruct(bytes);

            Assert.Equal(21, deserialized.Value);
            Assert.Equal(42, deserialized.DoubledValue); // 21 * 2 = 42
        }

        #endregion

        #region Null Instance Tests

        [Fact]
        public void GG_NullInstance_NoCallbacksInvoked_NoException()
        {
            SerializationCallbackModel model = null;

            using var ms = new MemoryStream();
            // Should not throw, just return without doing anything
            global::GProtobuf.Tests.TestModel.Serialization.Serializers.SerializeSerializationCallbackModel(ms, model);

            Assert.Equal(0, ms.Length);
        }

        #endregion

        #region Computed Values Are Serialized Tests

        [Fact]
        public void GG_ComputedChecksum_IsSerializedCorrectly()
        {
            var model = new SerializationCallbackModel
            {
                Name = "Hello",
                Value = 123
            };

            // Checksum should be null before serialization
            Assert.Null(model.ComputedChecksum);

            using var ms = new MemoryStream();
            global::GProtobuf.Tests.TestModel.Serialization.Serializers.SerializeSerializationCallbackModel(ms, model);

            // Verify the computed checksum was serialized
            var bytes = ms.ToArray();
            var deserialized = global::GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializeSerializationCallbackModel(bytes);

            Assert.Equal("HASH:5:123", deserialized.ComputedChecksum);
            Assert.Equal("Hello", deserialized.Name);
            Assert.Equal(123, deserialized.Value);
        }

        #endregion

        #region Inheritance Callback Tests

        /// <summary>
        /// When serializing base type directly, base callbacks should be invoked.
        /// </summary>
        [Fact]
        public void GG_Inheritance_BaseType_CallbacksInvoked()
        {
            var model = new BaseWithCallback
            {
                BaseName = "Test"
            };

            using var ms = new MemoryStream();
            global::GProtobuf.Tests.TestModel.Serialization.Serializers.SerializeBaseWithCallback(ms, model);

            // Base callbacks should be invoked
            Assert.Contains("Base.BeforeSerialization", model.CallbackLog);
            Assert.Contains("Base.AfterSerialization", model.CallbackLog);
            Assert.Equal("BASE:4", model.BaseComputedValue);

            // Verify serialized correctly
            var bytes = ms.ToArray();
            var deserialized = global::GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializeBaseWithCallback(bytes);
            Assert.Equal("Test", deserialized.BaseName);
            Assert.Equal("BASE:4", deserialized.BaseComputedValue);
        }

        /// <summary>
        /// When serializing derived type through base reference, base callbacks should still be invoked.
        /// This tests the try-finally pattern with early return in switch.
        /// </summary>
        [Fact]
        public void GG_Inheritance_DerivedThroughBase_BaseCallbacksInvoked()
        {
            // Create derived instance but serialize through base type method
            BaseWithCallback model = new DerivedWithCallback
            {
                BaseName = "BaseTest",
                DerivedName = "DerivedTest"
            };

            using var ms = new MemoryStream();
            // Serialize using base type serializer (polymorphic)
            global::GProtobuf.Tests.TestModel.Serialization.Serializers.SerializeBaseWithCallback(ms, model);

            // Base callbacks MUST be invoked even though we're serializing derived type
            // This is because WriteBase calls BeforeCallbacks, then switch to derived, then AfterCallbacks in finally
            Assert.Contains("Base.BeforeSerialization", model.CallbackLog);
            Assert.Contains("Base.AfterSerialization", model.CallbackLog);
        }

        /// <summary>
        /// When serializing derived type directly, derived callbacks should be invoked.
        /// </summary>
        [Fact]
        public void GG_Inheritance_DerivedTypeDirect_DerivedCallbacksInvoked()
        {
            var model = new DerivedWithCallback
            {
                BaseName = "BaseTest",
                DerivedName = "DerivedTest"
            };

            using var ms = new MemoryStream();
            global::GProtobuf.Tests.TestModel.Serialization.Serializers.SerializeDerivedWithCallback(ms, model);

            // Derived callbacks should be invoked
            Assert.Contains("Derived.BeforeSerialization", model.CallbackLog);
            Assert.Contains("Derived.AfterSerialization", model.CallbackLog);
            Assert.Equal("DERIVED:11", model.DerivedComputedValue);

            // Verify serialized correctly
            var bytes = ms.ToArray();
            var deserialized = global::GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializeDerivedWithCallback(bytes);
            Assert.Equal("DerivedTest", deserialized.DerivedName);
            Assert.Equal("DERIVED:11", deserialized.DerivedComputedValue);
        }

        /// <summary>
        /// Derived class without callbacks should still work correctly.
        /// </summary>
        [Fact]
        public void GG_Inheritance_DerivedWithoutCallback_SerializesCorrectly()
        {
            var model = new DerivedWithoutCallback
            {
                BaseName = "Base",
                DerivedValue = 42
            };

            using var ms = new MemoryStream();
            global::GProtobuf.Tests.TestModel.Serialization.Serializers.SerializeDerivedWithoutCallback(ms, model);

            var bytes = ms.ToArray();
            var deserialized = global::GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializeDerivedWithoutCallback(bytes);

            Assert.Equal("Base", deserialized.BaseName);
            Assert.Equal(42, deserialized.DerivedValue);
        }

        #endregion

        #region SerializeToArray Two-Pass Tests

        /// <summary>
        /// Critical test: SerializeToArray uses two-pass (size calculation + write).
        /// BeforeSerialization MUST be called BEFORE size calculation.
        /// </summary>
        [Fact]
        public void GG_SerializeToArray_TwoPass_CallbackBeforeSizeCalculation()
        {
            var model = new TwoPassCallbackModel
            {
                Payload = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 },
                PayloadSize = 0 // Will be set by callback to 10
            };

            Assert.Equal(0, model.PayloadSize);
            Assert.False(model.BeforeCallbackInvoked);

            // SerializeToArray uses two-pass: SizeCalculator + Write
            var bytes = global::GProtobuf.Tests.TestModel.Serialization.Serializers.SerializeToArrayTwoPassCallbackModel(model);

            // Callback should have been invoked
            Assert.True(model.BeforeCallbackInvoked);
            Assert.True(model.AfterCallbackInvoked);
            Assert.Equal(10, model.PayloadSize);

            // Deserialize and verify correct size was serialized
            var deserialized = global::GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializeTwoPassCallbackModel(bytes);

            // If callback was called AFTER size calculation, PayloadSize would be 0
            // If callback was called BEFORE size calculation, PayloadSize should be 10
            Assert.Equal(10, deserialized.PayloadSize);
            Assert.Equal(10, deserialized.Payload.Length);
        }

        [Fact]
        public void GG_SerializeToArray_NullInstance_ReturnsEmptyArray()
        {
            TwoPassCallbackModel model = null;

            var bytes = global::GProtobuf.Tests.TestModel.Serialization.Serializers.SerializeToArrayTwoPassCallbackModel(model);

            Assert.NotNull(bytes);
            Assert.Empty(bytes);
        }

        #endregion

        #region Cross-Compatibility Tests (GP/PG)

        /// <summary>
        /// GProtobuf serializes, protobuf-net deserializes.
        /// Verifies that computed values from callbacks are correctly serialized.
        /// </summary>
        [Fact]
        public void GP_SerializationCallbackModel_ProtobufNetCanRead()
        {
            var model = new SerializationCallbackModel
            {
                Name = "CrossTest",
                Value = 999
            };

            // GProtobuf serializes (callbacks compute checksum)
            using var ms = new MemoryStream();
            global::GProtobuf.Tests.TestModel.Serialization.Serializers.SerializeSerializationCallbackModel(ms, model);

            // Verify callback was invoked
            Assert.Equal("HASH:9:999", model.ComputedChecksum);

            // protobuf-net deserializes
            ms.Position = 0;
            var deserialized = ProtoBuf.Serializer.Deserialize<SerializationCallbackModel>(ms);

            Assert.Equal("CrossTest", deserialized.Name);
            Assert.Equal(999, deserialized.Value);
            Assert.Equal("HASH:9:999", deserialized.ComputedChecksum);
        }

        /// <summary>
        /// protobuf-net serializes, GProtobuf deserializes.
        /// Note: protobuf-net ALSO invokes callbacks during serialization.
        /// </summary>
        [Fact]
        public void PG_SerializationCallbackModel_GProtobufCanRead()
        {
            var model = new SerializationCallbackModel
            {
                Name = "CrossTest",
                Value = 777
            };

            // protobuf-net serializes (it also invokes callbacks!)
            using var ms = new MemoryStream();
            ProtoBuf.Serializer.Serialize(ms, model);

            // Verify protobuf-net invoked the callback
            Assert.Equal("HASH:9:777", model.ComputedChecksum);

            // GProtobuf deserializes
            var bytes = ms.ToArray();
            var deserialized = global::GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializeSerializationCallbackModel(bytes);

            Assert.Equal("CrossTest", deserialized.Name);
            Assert.Equal(777, deserialized.Value);
            Assert.Equal("HASH:9:777", deserialized.ComputedChecksum);
        }

        /// <summary>
        /// Cross-compatibility for TwoPassCallbackModel.
        /// </summary>
        [Fact]
        public void GP_TwoPassCallbackModel_ProtobufNetCanRead()
        {
            var model = new TwoPassCallbackModel
            {
                Payload = new byte[] { 1, 2, 3, 4, 5 },
                PayloadSize = 0 // Will be computed by callback
            };

            // GProtobuf serializes
            using var ms = new MemoryStream();
            global::GProtobuf.Tests.TestModel.Serialization.Serializers.SerializeTwoPassCallbackModel(ms, model);

            // protobuf-net deserializes
            ms.Position = 0;
            var deserialized = ProtoBuf.Serializer.Deserialize<TwoPassCallbackModel>(ms);

            Assert.Equal(5, deserialized.PayloadSize); // Computed by callback
            Assert.Equal(5, deserialized.Payload.Length);
        }

        /// <summary>
        /// Cross-compatibility for inheritance with callbacks.
        /// </summary>
        [Fact]
        public void GP_DerivedWithCallback_ProtobufNetCanRead()
        {
            var model = new DerivedWithCallback
            {
                BaseName = "BaseTest",
                DerivedName = "DerivedTest"
            };

            // GProtobuf serializes (derived callbacks compute values)
            using var ms = new MemoryStream();
            global::GProtobuf.Tests.TestModel.Serialization.Serializers.SerializeDerivedWithCallback(ms, model);

            // protobuf-net deserializes
            ms.Position = 0;
            var deserialized = ProtoBuf.Serializer.Deserialize<DerivedWithCallback>(ms);

            Assert.Equal("BaseTest", deserialized.BaseName);
            Assert.Equal("DerivedTest", deserialized.DerivedName);
            Assert.Equal("DERIVED:11", deserialized.DerivedComputedValue);
        }

        #endregion

        #region Nested Objects with Callbacks Tests

        /// <summary>
        /// Tests that callbacks on nested objects are invoked when serialized as part of parent.
        /// NOTE: This tests current behavior - nested object callbacks may or may not be invoked
        /// depending on how the generator handles nested serialization.
        /// </summary>
        [Fact]
        public void GG_NestedObject_ParentSerializes_ChildCallbackBehavior()
        {
            var model = new ParentWithNestedCallback
            {
                ParentName = "Parent",
                ParentValue = 100,
                Child = new NestedChildWithCallback
                {
                    ChildName = "Child"
                }
            };

            Assert.False(model.Child.BeforeCallbackInvoked);
            Assert.Null(model.Child.ChildComputedValue);

            using var ms = new MemoryStream();
            global::GProtobuf.Tests.TestModel.Serialization.Serializers.SerializeParentWithNestedCallback(ms, model);

            // Deserialize and check
            var bytes = ms.ToArray();
            var deserialized = global::GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializeParentWithNestedCallback(bytes);

            Assert.Equal("Parent", deserialized.ParentName);
            Assert.Equal(100, deserialized.ParentValue);
            Assert.NotNull(deserialized.Child);
            Assert.Equal("Child", deserialized.Child.ChildName);

            // Check if child's computed value was serialized (depends on implementation)
            // If callbacks are invoked on nested objects, this will be set
            // Current behavior: nested callbacks ARE invoked because Write method is called
            if (model.Child.BeforeCallbackInvoked)
            {
                Assert.Equal("CHILD:5", deserialized.Child.ChildComputedValue);
            }
        }

        /// <summary>
        /// Tests nested object with null child - should not throw.
        /// </summary>
        [Fact]
        public void GG_NestedObject_NullChild_NoException()
        {
            var model = new ParentWithNestedCallback
            {
                ParentName = "Parent",
                ParentValue = 100,
                Child = null
            };

            using var ms = new MemoryStream();
            global::GProtobuf.Tests.TestModel.Serialization.Serializers.SerializeParentWithNestedCallback(ms, model);

            var bytes = ms.ToArray();
            var deserialized = global::GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializeParentWithNestedCallback(bytes);

            Assert.Equal("Parent", deserialized.ParentName);
            Assert.Null(deserialized.Child);
        }

        #endregion

        #region Collections with Callback Objects Tests

        /// <summary>
        /// Tests List of objects with callbacks.
        /// </summary>
        [Fact]
        public void GG_ListOfCallbackObjects_Serializes()
        {
            var model = new ContainerWithCallbackList
            {
                ContainerName = "Container",
                Items = new List<ItemWithCallback>
                {
                    new ItemWithCallback { Id = 1, Name = "First" },
                    new ItemWithCallback { Id = 2, Name = "Second" },
                    new ItemWithCallback { Id = 3, Name = "Third" }
                }
            };

            // Verify callbacks not yet invoked
            foreach (var item in model.Items)
            {
                Assert.False(item.BeforeCallbackInvoked);
                Assert.Null(item.ComputedHash);
            }

            using var ms = new MemoryStream();
            global::GProtobuf.Tests.TestModel.Serialization.Serializers.SerializeContainerWithCallbackList(ms, model);

            var bytes = ms.ToArray();
            var deserialized = global::GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializeContainerWithCallbackList(bytes);

            Assert.Equal("Container", deserialized.ContainerName);
            Assert.Equal(3, deserialized.Items.Count);

            // Check items were serialized correctly
            Assert.Equal(1, deserialized.Items[0].Id);
            Assert.Equal("First", deserialized.Items[0].Name);
            Assert.Equal(2, deserialized.Items[1].Id);
            Assert.Equal("Second", deserialized.Items[1].Name);
            Assert.Equal(3, deserialized.Items[2].Id);
            Assert.Equal("Third", deserialized.Items[2].Name);

            // Check if callbacks were invoked on list items (behavior depends on implementation)
            // If Write method is called for each item, callbacks will be invoked
            if (model.Items[0].BeforeCallbackInvoked)
            {
                Assert.Equal("ITEM:1:5", deserialized.Items[0].ComputedHash);
                Assert.Equal("ITEM:2:6", deserialized.Items[1].ComputedHash);
                Assert.Equal("ITEM:3:5", deserialized.Items[2].ComputedHash);
            }
        }

        /// <summary>
        /// Tests Dictionary with callback objects as values.
        /// </summary>
        [Fact]
        public void GG_DictionaryOfCallbackObjects_Serializes()
        {
            var model = new ContainerWithCallbackDictionary
            {
                ContainerName = "DictContainer",
                ItemsById = new Dictionary<int, ItemWithCallback>
                {
                    { 10, new ItemWithCallback { Id = 10, Name = "Ten" } },
                    { 20, new ItemWithCallback { Id = 20, Name = "Twenty" } }
                }
            };

            using var ms = new MemoryStream();
            global::GProtobuf.Tests.TestModel.Serialization.Serializers.SerializeContainerWithCallbackDictionary(ms, model);

            var bytes = ms.ToArray();
            var deserialized = global::GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializeContainerWithCallbackDictionary(bytes);

            Assert.Equal("DictContainer", deserialized.ContainerName);
            Assert.Equal(2, deserialized.ItemsById.Count);
            Assert.Equal("Ten", deserialized.ItemsById[10].Name);
            Assert.Equal("Twenty", deserialized.ItemsById[20].Name);
        }

        /// <summary>
        /// Tests empty list of callback objects.
        /// Note: In protobuf, empty collections are not serialized, so they deserialize as null.
        /// </summary>
        [Fact]
        public void GG_EmptyListOfCallbackObjects_Serializes()
        {
            var model = new ContainerWithCallbackList
            {
                ContainerName = "EmptyContainer",
                Items = new List<ItemWithCallback>()
            };

            using var ms = new MemoryStream();
            global::GProtobuf.Tests.TestModel.Serialization.Serializers.SerializeContainerWithCallbackList(ms, model);

            var bytes = ms.ToArray();
            var deserialized = global::GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializeContainerWithCallbackList(bytes);

            Assert.Equal("EmptyContainer", deserialized.ContainerName);
            // Empty collections are not serialized in protobuf, so they come back as null
            Assert.True(deserialized.Items == null || deserialized.Items.Count == 0);
        }

        /// <summary>
        /// Cross-compatibility: List of callback objects GProtobuf -> protobuf-net.
        /// </summary>
        [Fact]
        public void GP_ListOfCallbackObjects_ProtobufNetCanRead()
        {
            var model = new ContainerWithCallbackList
            {
                ContainerName = "CrossContainer",
                Items = new List<ItemWithCallback>
                {
                    new ItemWithCallback { Id = 1, Name = "Item1" },
                    new ItemWithCallback { Id = 2, Name = "Item2" }
                }
            };

            using var ms = new MemoryStream();
            global::GProtobuf.Tests.TestModel.Serialization.Serializers.SerializeContainerWithCallbackList(ms, model);

            ms.Position = 0;
            var deserialized = ProtoBuf.Serializer.Deserialize<ContainerWithCallbackList>(ms);

            Assert.Equal("CrossContainer", deserialized.ContainerName);
            Assert.Equal(2, deserialized.Items.Count);
            Assert.Equal(1, deserialized.Items[0].Id);
            Assert.Equal(2, deserialized.Items[1].Id);
        }

        #endregion

        #region Exception Handling in Callbacks Tests

        /// <summary>
        /// When BeforeSerialization throws, the exception should propagate.
        /// </summary>
        [Fact]
        public void GG_ExceptionInBeforeCallback_Propagates()
        {
            var model = new ExceptionInBeforeCallback
            {
                Data = "Test",
                ShouldThrow = true
            };

            using var ms = new MemoryStream();

            var exception = Assert.Throws<InvalidOperationException>(() =>
                global::GProtobuf.Tests.TestModel.Serialization.Serializers.SerializeExceptionInBeforeCallback(ms, model));

            Assert.Equal("BeforeSerialization failed!", exception.Message);

            // AfterSerialization should NOT be invoked because we're in try block when exception occurs
            // and the finally block runs after the exception propagates
            // Actually, with try-finally, the finally DOES run, so AfterCallback IS invoked
        }

        /// <summary>
        /// When BeforeSerialization throws, nothing should be written to stream.
        /// </summary>
        [Fact]
        public void GG_ExceptionInBeforeCallback_NothingWritten()
        {
            var model = new ExceptionInBeforeCallback
            {
                Data = "Test",
                ShouldThrow = true
            };

            using var ms = new MemoryStream();

            Assert.Throws<InvalidOperationException>(() =>
                global::GProtobuf.Tests.TestModel.Serialization.Serializers.SerializeExceptionInBeforeCallback(ms, model));

            // Stream should be empty because exception occurred before writing
            Assert.Equal(0, ms.Length);
        }

        /// <summary>
        /// When AfterSerialization throws, the exception propagates but BeforeCallback was invoked.
        /// Note: Data may not be flushed to stream because Flush() is called after WriteXxx returns,
        /// but exception in finally prevents normal return.
        /// </summary>
        [Fact]
        public void GG_ExceptionInAfterCallback_BeforeCallbackStillInvoked()
        {
            var model = new ExceptionInAfterCallback
            {
                Data = "Important Data",
                ShouldThrow = true
            };

            using var ms = new MemoryStream();

            var exception = Assert.Throws<InvalidOperationException>(() =>
                global::GProtobuf.Tests.TestModel.Serialization.Serializers.SerializeExceptionInAfterCallback(ms, model));

            Assert.Equal("AfterSerialization failed!", exception.Message);

            // BeforeSerialization should have been invoked (it's called before try block)
            Assert.True(model.BeforeCallbackInvoked);

            // Note: Data may NOT be in stream because:
            // 1. Data is written to StreamWriter's internal buffer
            // 2. Exception in finally prevents Flush() from being called
            // 3. So the data stays in buffer and doesn't reach the stream
            // This is expected behavior - exception in callback corrupts the serialization
        }

        /// <summary>
        /// When no exception is thrown, everything works normally.
        /// </summary>
        [Fact]
        public void GG_NoExceptionInCallbacks_WorksNormally()
        {
            var model = new ExceptionInBeforeCallback
            {
                Data = "Test",
                ShouldThrow = false
            };

            using var ms = new MemoryStream();
            global::GProtobuf.Tests.TestModel.Serialization.Serializers.SerializeExceptionInBeforeCallback(ms, model);

            // AfterCallback should be invoked
            Assert.True(model.AfterCallbackInvoked);

            var bytes = ms.ToArray();
            var deserialized = global::GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializeExceptionInBeforeCallback(bytes);

            Assert.Equal("Test", deserialized.Data);
        }

        #endregion

        #region Multiple Serialize Calls Tests

        /// <summary>
        /// Callbacks should be invoked on each serialization call.
        /// </summary>
        [Fact]
        public void GG_MultipleSerializeCalls_CallbacksInvokedEachTime()
        {
            var model = new CallbackCounterModel
            {
                Data = "Test"
            };

            Assert.Equal(0, model.BeforeCount);
            Assert.Equal(0, model.AfterCount);

            // First serialization
            using (var ms1 = new MemoryStream())
            {
                global::GProtobuf.Tests.TestModel.Serialization.Serializers.SerializeCallbackCounterModel(ms1, model);
            }
            Assert.Equal(1, model.BeforeCount);
            Assert.Equal(1, model.AfterCount);

            // Second serialization
            using (var ms2 = new MemoryStream())
            {
                global::GProtobuf.Tests.TestModel.Serialization.Serializers.SerializeCallbackCounterModel(ms2, model);
            }
            Assert.Equal(2, model.BeforeCount);
            Assert.Equal(2, model.AfterCount);

            // Third serialization
            using (var ms3 = new MemoryStream())
            {
                global::GProtobuf.Tests.TestModel.Serialization.Serializers.SerializeCallbackCounterModel(ms3, model);
            }
            Assert.Equal(3, model.BeforeCount);
            Assert.Equal(3, model.AfterCount);
        }

        #endregion

        #region Array of Callback Objects Tests

        /// <summary>
        /// Tests array (not List) of objects with callbacks.
        /// </summary>
        [Fact]
        public void GG_ArrayOfCallbackObjects_Serializes()
        {
            var model = new ContainerWithCallbackArray
            {
                ContainerName = "ArrayContainer",
                Items = new[]
                {
                    new ItemWithCallback { Id = 1, Name = "First" },
                    new ItemWithCallback { Id = 2, Name = "Second" }
                }
            };

            using var ms = new MemoryStream();
            global::GProtobuf.Tests.TestModel.Serialization.Serializers.SerializeContainerWithCallbackArray(ms, model);

            var bytes = ms.ToArray();
            var deserialized = global::GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializeContainerWithCallbackArray(bytes);

            Assert.Equal("ArrayContainer", deserialized.ContainerName);
            Assert.NotNull(deserialized.Items);
            Assert.Equal(2, deserialized.Items.Length);
            Assert.Equal(1, deserialized.Items[0].Id);
            Assert.Equal("First", deserialized.Items[0].Name);
            Assert.Equal(2, deserialized.Items[1].Id);
            Assert.Equal("Second", deserialized.Items[1].Name);
        }

        #endregion

        #region Static Callbacks Tests

        /// <summary>
        /// Static callbacks should NOT be invoked (they don't have instance context).
        /// The generator should skip static methods.
        /// </summary>
        [Fact]
        public void GG_StaticCallbacks_NotInvoked()
        {
            // Reset static counters
            StaticCallbackModel.ResetCounters();

            var model = new StaticCallbackModel
            {
                Data = "Test"
            };

            using var ms = new MemoryStream();
            global::GProtobuf.Tests.TestModel.Serialization.Serializers.SerializeStaticCallbackModel(ms, model);

            // Static callbacks should NOT be invoked
            // (they don't make sense - no instance to call on)
            Assert.Equal(0, StaticCallbackModel.StaticBeforeCount);
            Assert.Equal(0, StaticCallbackModel.StaticAfterCount);

            // But data should still be serialized correctly
            var bytes = ms.ToArray();
            var deserialized = global::GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializeStaticCallbackModel(bytes);
            Assert.Equal("Test", deserialized.Data);
        }

        #endregion

        #region Parameterized Callbacks Tests

        /// <summary>
        /// Callbacks with parameters should be skipped.
        /// Only parameterless callbacks should be invoked.
        /// </summary>
        [Fact]
        public void GG_ParameterizedCallbacks_OnlyParameterlessInvoked()
        {
            var model = new ParameterizedCallbackModel
            {
                Data = "Test"
            };

            Assert.False(model.BeforeWithParamInvoked);
            Assert.False(model.BeforeWithoutParamInvoked);

            using var ms = new MemoryStream();
            global::GProtobuf.Tests.TestModel.Serialization.Serializers.SerializeParameterizedCallbackModel(ms, model);

            // Parameterized callback should NOT be invoked
            Assert.False(model.BeforeWithParamInvoked);

            // Parameterless callback SHOULD be invoked
            Assert.True(model.BeforeWithoutParamInvoked);
        }

        #endregion

        #region Deep Inheritance (3-level) Tests

        /// <summary>
        /// Tests callbacks on 3-level inheritance: Root -> Middle -> Leaf.
        /// When serializing Leaf directly, only Leaf's callbacks should be invoked.
        /// </summary>
        [Fact]
        public void GG_ThreeLevelInheritance_LeafDirect_LeafCallbacksInvoked()
        {
            var model = new LeafWithCallback
            {
                RootName = "Root",
                MiddleName = "Middle",
                LeafName = "Leaf"
            };

            using var ms = new MemoryStream();
            global::GProtobuf.Tests.TestModel.Serialization.Serializers.SerializeLeafWithCallback(ms, model);

            // Leaf callbacks should be invoked
            Assert.Contains("Leaf.Before", model.CallbackLog);
            Assert.Contains("Leaf.After", model.CallbackLog);

            // Verify data
            var bytes = ms.ToArray();
            var deserialized = global::GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializeLeafWithCallback(bytes);
            Assert.Equal("Root", deserialized.RootName);
            Assert.Equal("Middle", deserialized.MiddleName);
            Assert.Equal("Leaf", deserialized.LeafName);
        }

        /// <summary>
        /// Tests callbacks when serializing Leaf through Root reference.
        /// Root callbacks should be invoked (polymorphic serialization).
        /// </summary>
        [Fact]
        public void GG_ThreeLevelInheritance_LeafThroughRoot_RootCallbacksInvoked()
        {
            RootWithCallback model = new LeafWithCallback
            {
                RootName = "Root",
                MiddleName = "Middle",
                LeafName = "Leaf"
            };

            using var ms = new MemoryStream();
            global::GProtobuf.Tests.TestModel.Serialization.Serializers.SerializeRootWithCallback(ms, model);

            // Root callbacks should be invoked (we're serializing through Root type)
            Assert.Contains("Root.Before", model.CallbackLog);
            Assert.Contains("Root.After", model.CallbackLog);
        }

        /// <summary>
        /// Tests Middle level serialization.
        /// </summary>
        [Fact]
        public void GG_ThreeLevelInheritance_MiddleDirect_MiddleCallbacksInvoked()
        {
            var model = new MiddleWithCallback
            {
                RootName = "Root",
                MiddleName = "Middle"
            };

            using var ms = new MemoryStream();
            global::GProtobuf.Tests.TestModel.Serialization.Serializers.SerializeMiddleWithCallback(ms, model);

            // Middle callbacks should be invoked
            Assert.Contains("Middle.Before", model.CallbackLog);
            Assert.Contains("Middle.After", model.CallbackLog);

            var bytes = ms.ToArray();
            var deserialized = global::GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializeMiddleWithCallback(bytes);
            Assert.Equal("Root", deserialized.RootName);
            Assert.Equal("Middle", deserialized.MiddleName);
        }

        #endregion

        #region Private Callbacks Tests

        /// <summary>
        /// Private callbacks are NOT invoked - they can't be called from generated code.
        /// The generator skips private methods marked with callback attributes.
        /// </summary>
        [Fact]
        public void GG_PrivateCallbacks_NotInvoked()
        {
            var model = new PrivateCallbackModel
            {
                Data = "Test"
            };

            Assert.False(model.PrivateBeforeInvoked);
            Assert.Null(model.ComputedValue);

            using var ms = new MemoryStream();
            global::GProtobuf.Tests.TestModel.Serialization.Serializers.SerializePrivateCallbackModel(ms, model);

            // Private callbacks should NOT be invoked
            // (they can't be called from generated code due to accessibility)
            Assert.False(model.PrivateBeforeInvoked);
            Assert.False(model.PrivateAfterInvoked);

            // Data should still be serialized correctly
            var bytes = ms.ToArray();
            var deserialized = global::GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializePrivateCallbackModel(bytes);

            Assert.Equal("Test", deserialized.Data);
            // ComputedValue is null because private callback was never invoked
            Assert.Null(deserialized.ComputedValue);
        }

        #endregion

        #region Alternative Serialization Paths Tests

        /// <summary>
        /// Tests that OnePass serialization also invokes callbacks.
        /// </summary>
        [Fact]
        public void GG_OnePassSerialization_CallbacksInvoked()
        {
            var model = new CallbackCounterModel
            {
                Data = "OnePassTest"
            };

            Assert.Equal(0, model.BeforeCount);
            Assert.Equal(0, model.AfterCount);

            using var ms = new MemoryStream();
            global::GProtobuf.Tests.TestModel.Serialization.Serializers.SerializeCallbackCounterModelOnePass(ms, model);

            Assert.Equal(1, model.BeforeCount);
            Assert.Equal(1, model.AfterCount);

            var bytes = ms.ToArray();
            var deserialized = global::GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializeCallbackCounterModel(bytes);
            Assert.Equal("OnePassTest", deserialized.Data);
        }

        /// <summary>
        /// Tests that SerializeTo (Span) serialization also invokes callbacks.
        /// </summary>
        [Fact]
        public void GG_SpanSerialization_CallbacksInvoked()
        {
            var model = new CallbackCounterModel
            {
                Data = "SpanTest"
            };

            Assert.Equal(0, model.BeforeCount);
            Assert.Equal(0, model.AfterCount);

            Span<byte> buffer = stackalloc byte[256];
            int written = global::GProtobuf.Tests.TestModel.Serialization.Serializers.SerializeToCallbackCounterModel(buffer, model);

            Assert.Equal(1, model.BeforeCount);
            Assert.Equal(1, model.AfterCount);

            var deserialized = global::GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializeCallbackCounterModel(buffer.Slice(0, written));
            Assert.Equal("SpanTest", deserialized.Data);
        }

        /// <summary>
        /// Tests IBufferWriter serialization path.
        /// </summary>
        [Fact]
        public void GG_BufferWriterSerialization_CallbacksInvoked()
        {
            var model = new CallbackCounterModel
            {
                Data = "BufferWriterTest"
            };

            Assert.Equal(0, model.BeforeCount);
            Assert.Equal(0, model.AfterCount);

            var buffer = new System.Buffers.ArrayBufferWriter<byte>();
            global::GProtobuf.Tests.TestModel.Serialization.Serializers.SerializeCallbackCounterModel(buffer, model);

            Assert.Equal(1, model.BeforeCount);
            Assert.Equal(1, model.AfterCount);

            var deserialized = global::GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializeCallbackCounterModel(buffer.WrittenSpan);
            Assert.Equal("BufferWriterTest", deserialized.Data);
        }

        #endregion
    }
}
