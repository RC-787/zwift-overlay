using PcapDotNet.Core;
using PcapDotNet.Packets;
using ProtoBuf;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace ZwiftMetricsUI {
    public static class ZwiftUdpPacketUtils {
        public static int ZwiftOutgoingPortNumber = 3022;

        public static LivePacketDevice GetZwiftNetworkAdapter() {
            Debug.WriteLine("Detecting Network Devices...");

            // Retrieve the device list from the local machine
            IList<LivePacketDevice> allDevices = LivePacketDevice.AllLocalMachine;
            Debug.WriteLine("Found {0} Network Devices.", allDevices.Count);

            if(allDevices.Count == 1) {
                Debug.WriteLine("Found Zwift Network Device: {0}", allDevices[0].Description);
                return allDevices[0];
            }
            else {
                // If more than one network device is detected, we need to figure out which one is being used by Zwift
                foreach(LivePacketDevice networkDevice in allDevices) {
                    if (IsNetworkDeviceUsedByZwift(networkDevice)) {
                        return networkDevice;
                    }
                }
            }

            return null;
        }

        private static bool IsNetworkDeviceUsedByZwift(LivePacketDevice networkDevice) {
            using (PacketCommunicator communicator = networkDevice.Open(65536, PacketDeviceOpenAttributes.Promiscuous, 1000)) {
                communicator.SetFilter(String.Format("udp dst port {0}", ZwiftOutgoingPortNumber));

                Packet testPacket;
                PacketCommunicatorReceiveResult result = communicator.ReceivePacket(out testPacket);

                switch (result) {
                    case PacketCommunicatorReceiveResult.Timeout:
                        Debug.WriteLine("Timeout occured while attempting to view Zwift UDP packet on Port {0} for {1}",
                            ZwiftOutgoingPortNumber, networkDevice.Description);
                        return false;
                    case PacketCommunicatorReceiveResult.Ok:
                        Debug.WriteLine("Successfully viewed Zwift UDP packet on Port {0} for {1}",
                            ZwiftOutgoingPortNumber, networkDevice.Description);
                        return true;
                    default:
                        return false;
                }
            }
        }

        public static ZwiftOutgoingUdpDataPacket DecodeHexString(string hexString) {
            // Note: I am having to manually decode this because the Zwift outgoing ProtoBuf UDP packet seems to contain
            // a invalid wire type which is causing Serialization via ProtoBuf-net to fail. The invalid wire type can also be seen
            // by viewing Zwift UDP packets in WireShark

            Dictionary<int, object> fields = GetFieldValues(hexString);
            Dictionary<int, object> playerState = fields[7] as Dictionary<int, object>;

            ZwiftOutgoingUdpDataPacket result = new ZwiftOutgoingUdpDataPacket {
                ConnectionStatusId = Convert.ToInt32(fields[1]),
                ZwiftUserId = Convert.ToInt32(fields[2]),
                ZwiftWorldUnixTimestamp = Convert.ToInt64(fields[3]),
                Distance = Convert.ToInt64(playerState[3]),
                Speed = Convert.ToInt32(playerState[6]),
                Cadence = Convert.ToInt32(playerState[9]),
                HeartRate = Convert.ToInt32(playerState[11]),
                Power = Convert.ToInt32(playerState[12]),
                ElevationGain = Convert.ToInt64(playerState[15]),
            };

            return result;
        }

        private static Dictionary<int, object> GetFieldValues(string hexString) {
            // TODO: Use byte[] instead of strings

            // Convert the hex to a binary string
            string binary = String.Join(String.Empty, hexString.Select(
                c => Convert.ToString(Convert.ToInt32(c.ToString(), 16), 2).PadLeft(4, '0')
            ));

            // Split the binary into bytes
            List<String> binaryAsBytes = new List<string>();
            int currentByteNumber = 1;
            string byteSubstring;
            while((currentByteNumber * 8) <= binary.Length) {
                byteSubstring = binary.Substring((currentByteNumber - 1) * 8, 8);
                binaryAsBytes.Add(byteSubstring);
                currentByteNumber++;
            }

            Dictionary<int, object> result = new Dictionary<int, object>();

            // Decode the payload according to the ProtoBuf protocol
            bool fieldMetdataHasBeenCalculated = false;
            bool isNestedObject = false;
            int fieldNumber = 0;
            int wireType = 0;
            List<string> arrayOfFieldValues = new List<string>();
            long fieldValue = 0;
            for(int i = 0; i < binaryAsBytes.Count; i++) {
                string byteAsString = binaryAsBytes[i];

                if(byteAsString[0] == '0' && !fieldMetdataHasBeenCalculated) {
                    // If we get to here, then the first bit of the byte is 0 and this is the byte which contains metadata about the field

                    // The next 4 bits are the Field Number
                    fieldNumber = (int.Parse(byteAsString[1].ToString()) * 8) + (int.Parse(byteAsString[2].ToString()) * 4) 
                        + (int.Parse(byteAsString[3].ToString()) * 2) + (int.Parse(byteAsString[4].ToString()));

                    // The last 3 bits are the Wire Type
                    wireType = (int.Parse(byteAsString[5].ToString()) * 4) + (int.Parse(byteAsString[6].ToString()) * 2) 
                        + (int.Parse(byteAsString[7].ToString()));

                    isNestedObject = (wireType == 2);

                    // The field value will be extracted from one or more subsequent bytes
                    fieldMetdataHasBeenCalculated = true;
                }
                else if (isNestedObject) {
                    // First, find out how many bytes are used by the nested object
                    int numberOfBytesInNestedObject =  Convert.ToInt32(byteAsString, 2);

                    // Decode the nested object
                    Dictionary<int, object> nestedObject = DecodeNestedObject(binaryAsBytes.Skip(i + 1).Take(numberOfBytesInNestedObject).ToArray());

                    // Add the nested object to the dictionary
                    if (!result.ContainsKey(fieldNumber)) {
                        result.Add(fieldNumber, nestedObject);
                    }

                    // Increment the for loop so that we don't decode the same bytes twice
                    i += numberOfBytesInNestedObject;

                    // Reset all of the variables
                    fieldMetdataHasBeenCalculated = false;
                    fieldNumber = 0;
                    wireType = 0;
                    arrayOfFieldValues = new List<string>();
                    fieldValue = 0;
                    isNestedObject = false;
                }
                else if(byteAsString[0] == '0' && fieldMetdataHasBeenCalculated) {
                    // If we get to here, then the first bit of the byte is 0 and the field metadata has already been retrieved

                    // Since the first bit of this byte is zero, this is the last byte for the current field
                    arrayOfFieldValues.Add(byteAsString.Substring(1, 7));

                    // Calculate the final value of the current field
                    fieldValue = CalculateFieldValue(arrayOfFieldValues);

                    // Add the field number and field value to the dictionary. Only add if it doesn't already exist
                    if (!result.ContainsKey(fieldNumber)) {
                        result.Add(fieldNumber, fieldValue);
                    }

                    // Reset all of the variables
                    fieldMetdataHasBeenCalculated = false;
                    fieldNumber = 0;
                    wireType = 0;
                    arrayOfFieldValues = new List<string>();
                    fieldValue = 0;
                }
                else {
                    // If we get to here, then the first bit of the byte is 1 and the field metadata has already been retrieved

                    // The leading 1 means that the this is NOT the last byte for the current field
                    // Drop the leading 1 so that we can accuratly calculate the field value
                    arrayOfFieldValues.Add(byteAsString.Substring(1, 7));
                }
            }

            return result;
        }

        private static Dictionary<int, object> DecodeNestedObject(string[] bytes) {
            Dictionary<int, object> result = new Dictionary<int, object>();

            // Decode the payload according to the ProtoBuf protocol
            bool fieldMetdataHasBeenCalculated = false;
            int fieldNumber = 0;
            int wireType = 0;
            List<string> arrayOfFieldValues = new List<string>();
            long fieldValue = 0;
            for (int i = 0; i < bytes.Length; i++) {
                string byteAsString = bytes[i];

                if (byteAsString[0] == '0' && !fieldMetdataHasBeenCalculated) {
                    // If we get to here, then the first bit of the byte is 0 and this is the byte which contains metadata about the field

                    // The next 4 bits are the Field Number
                    fieldNumber = (int.Parse(byteAsString[1].ToString()) * 8) + (int.Parse(byteAsString[2].ToString()) * 4)
                        + (int.Parse(byteAsString[3].ToString()) * 2) + (int.Parse(byteAsString[4].ToString()));

                    // The last 3 bits are the Wire Type
                    wireType = (int.Parse(byteAsString[5].ToString()) * 4) + (int.Parse(byteAsString[6].ToString()) * 2)
                        + (int.Parse(byteAsString[7].ToString()));

                    // The field value will be extracted from one or more subsequent bytes
                    fieldMetdataHasBeenCalculated = true;
                }
                else if (byteAsString[0] == '0' && fieldMetdataHasBeenCalculated) {
                    // If we get to here, then the first bit of the byte is 0 and the field metadata has already been retrieved

                    // Since the first bit of this byte is zero, this is the last byte for the current field
                    arrayOfFieldValues.Add(byteAsString.Substring(1, 7));

                    // Calculate the final value of the current field
                    fieldValue = CalculateFieldValue(arrayOfFieldValues);

                    // Add the field number and field value to the dictionary. Only add if it doesn't already exist
                    if (!result.ContainsKey(fieldNumber)) {
                        result.Add(fieldNumber, fieldValue);
                    }

                    // Reset all of the variables
                    fieldMetdataHasBeenCalculated = false;
                    fieldNumber = 0;
                    wireType = 0;
                    arrayOfFieldValues = new List<string>();
                    fieldValue = 0;
                }
                else {
                    // If we get to here, then the first bit of the byte is 1 and the field metadata has already been retrieved

                    // The leading 1 means that the this is NOT the last byte for the current field
                    // Drop the leading 1 so that we can accuratly calculate the field value
                    arrayOfFieldValues.Add(byteAsString.Substring(1, 7));
                }
            }

            return result;
        }

        private static long CalculateFieldValue(List<string> arrayOfFieldValues) {
            // As per the ProtoBuf protocol, we need to reverse the order when calculating the value
            arrayOfFieldValues.Reverse();

            string output = "";

            for(int i = 0; i < arrayOfFieldValues.Count; i++) {
                output += arrayOfFieldValues[i];
            }

            return Convert.ToInt64(output, 2);
        }
    }
}
