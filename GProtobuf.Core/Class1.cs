//namespace GProtobuf.Core
//{
//    public static class Utils
//    {
//        public static (int result, int bytesAdvanced) GetIntFromVarInt(ReadOnlySpan<byte> data)
//        {
//            byte position = 0;
//            byte resultPosition = 0;

//            int result = 0;

//            do
//            {
//                var d = data[position++];
//                result |= (d & 0b1111111) << resultPosition;
//                resultPosition += 7;
//                if ((d & 128) == 0)
//                    break;
//            } while (true);

//            return (result, position);
//        }
//    }
//}
