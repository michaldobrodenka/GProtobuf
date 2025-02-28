namespace GProtobuf.Tests
{
    public class UnitTest1
    {
        

        public UnitTest1()
        {

        }

        [Fact]
        public void EmptyArrayNullObject()
        {
            byte[] serializedData = Array.Empty<byte>();
            var result = Model.Serialization.Deserializers.DeserializeModelClassBase(serializedData);

            Assert.True(result == null);
        }
    }
}