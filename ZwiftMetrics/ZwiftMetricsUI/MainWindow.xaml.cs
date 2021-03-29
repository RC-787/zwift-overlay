using PcapDotNet.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using PcapDotNet.Packets;
using PcapDotNet.Packets.Transport;
using System.Threading;
using ProtoBuf;
using ProtoBuf.Meta;

namespace ZwiftMetricsUI {
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window {
        private Stopwatch _lapTime; // Used to record the current lap time
        private DispatcherTimer _dispatcherTimer; // Used for creating an event that will run every 1 second

        // Power
        private int _totalPowerForCurrentLap;
        private int _currentPower;
        private int _maxPower;
        private int _powerEventCount;

        // Heart rate
        private int _totalHeartbeatsForCurrentLap;
        private int _currentHeartRate;
        private int _maxHeartRate;
        private int _heartRateEventCount;


        public MainWindow() {
            ConfigureZwiftUdpPacketViewer();
            InitializeComponent();

            _lapTime = new Stopwatch();
            _dispatcherTimer = new DispatcherTimer();
            _dispatcherTimer.Tick += new EventHandler(ExecuteEverySecond);
            _dispatcherTimer.Interval = new TimeSpan(0, 0, 1);
            _dispatcherTimer.Start();
        }

        private void ConfigureZwiftUdpPacketViewer() {
            // Retrieve the network device that is being used by Zwift
            LivePacketDevice zwiftNetworkDevice = null;
            try {
                zwiftNetworkDevice = ZwiftUdpPacketUtils.GetZwiftNetworkAdapter();
            }
            catch (Exception e) {
                MessageBox.Show("ERROR:\nUnable to view Network devices.\nPlease make sure that Winpcap OR libpcap are installed on your machine.\nThe application will now close.",
                    "ZwiftMetrics", MessageBoxButton.OK, MessageBoxImage.Error);
                Application.Current.Shutdown();
                return;
            }

            if (zwiftNetworkDevice == null) {
                MessageBox.Show("ERROR:\nCould not detect Zwift UDP Packets on any Network devices.\nPlease make sure that you are logged in to Zwift and try again.\nThe application will now close.",
                    "ZwiftMetrics", MessageBoxButton.OK, MessageBoxImage.Error);
                Application.Current.Shutdown();
                return;
            }

            // Decode the Zwift UDP packets on a seperate thread so that the UI won't be blocked
            Thread zwiftUdpPacketDecodeThread = new Thread(() => ProcessZwiftUdpPackets(zwiftNetworkDevice));
            zwiftUdpPacketDecodeThread.Start();
        }

        private void ProcessZwiftUdpPackets(LivePacketDevice zwiftNetworkDevice) {
            using (PacketCommunicator communicator = zwiftNetworkDevice.Open(65536, PacketDeviceOpenAttributes.Promiscuous, 1000)) {
                communicator.SetFilter(String.Format("udp dst port {0}", ZwiftUdpPacketUtils.ZwiftOutgoingPortNumber));
                PacketCommunicatorReceiveResult result = communicator.ReceivePackets(-1, ZwiftPacketHandler);
            }
        }

        private void ZwiftPacketHandler(Packet packet) {
            UdpDatagram udpPacket = packet.Ethernet.IpV4.Udp;
            Debug.WriteLine("Zwift UDP Packet Payload Length={0}", udpPacket.Payload.Length);

            ZwiftOutgoingUdpDataPacket result = ZwiftUdpPacketUtils.DecodeHexString(udpPacket.Payload.ToHexadecimalString());

            // Update fields
            _currentHeartRate = result.HeartRate;
            if(_currentHeartRate > _maxHeartRate) {
                _maxHeartRate = _currentHeartRate;
            }
            _currentPower = result.Power;
            if(_currentPower > _maxPower) {
                _maxPower = _currentPower;
            }
        }


        private void ExecuteEverySecond(object sender, EventArgs e) {
            // Only update the values if the timer is actually running
            if (_lapTime.IsRunning) {

                // Update the laptime
                Label_LapTime.Content = String.Format("{0}:{1}:{2}",
                    _lapTime.Elapsed.Hours.ToString("00"),
                    _lapTime.Elapsed.Minutes.ToString("00"),
                    _lapTime.Elapsed.Seconds.ToString("00")
                );

                // Update the current power
                Label_Power.Content = String.Format("{0}w", _currentPower);
                Debug.WriteLine("Current Power: {0}w", _currentPower);

                // Update the average power
                _totalPowerForCurrentLap += _currentPower;
                _powerEventCount++;
                Debug.WriteLine("Total Watts: {0}w", _totalPowerForCurrentLap);
                int averagePower = (int) Math.Round((_totalPowerForCurrentLap / _powerEventCount * 1.0), 0, MidpointRounding.AwayFromZero);
                Debug.WriteLine("Average Power: {0}w ({1}/{2})", averagePower, _totalPowerForCurrentLap, _powerEventCount);
                Label_AvgPower.Content = String.Format("{0}w", averagePower);

                // Update the max power
                Label_MaxPower.Content = String.Format("{0}w", _maxPower);

                // Update the current heart rate
                Label_HeartRate.Content = String.Format("{0}", _currentHeartRate);
                Debug.WriteLine("Current Heart Rate: {0}", _currentHeartRate);

                // Update the average heart rate
                _totalHeartbeatsForCurrentLap += _currentHeartRate;
                _heartRateEventCount++;
                Debug.WriteLine("Total Heart Beats: {0}", _totalHeartbeatsForCurrentLap);
                int averageHeartRate = (int) Math.Round((_totalHeartbeatsForCurrentLap / _heartRateEventCount * 1.0), 0, MidpointRounding.AwayFromZero);
                Debug.WriteLine("Average Heart Rate: {0} ({1}/{2})", averageHeartRate, _totalHeartbeatsForCurrentLap, _heartRateEventCount);
                Label_AvgHR.Content = String.Format("{0}", averageHeartRate);

                // Update the max heart rate
                Label_MaxHR.Content = String.Format("{0}", _maxHeartRate);
            }
        }

        private void Button_Exit_Click(object sender, RoutedEventArgs e) {
            Application.Current.Shutdown();
        }

        private void BackgroundImage_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
            // Drag and drop the device to the desired position
            this.DragMove();
        }

        private void Button_Start_Click(object sender, RoutedEventArgs e) {
            if (_lapTime.IsRunning) {
                // Pause the timer
                _lapTime.Stop();
            }
            else {
                // Start OR resume the timer
                _lapTime.Start();
            }
        }

        private void Button_Restart_Click(object sender, RoutedEventArgs e) {
            _lapTime.Reset();
            _totalHeartbeatsForCurrentLap = 0;
            _heartRateEventCount = 0;
            _maxHeartRate = 0;
            _totalPowerForCurrentLap = 0;
            _powerEventCount = 0;
            _maxPower = 0;
            _lapTime.Start();
        }

        private void InvokeDragDropEvent(object sender, MouseButtonEventArgs e) {
            this.DragMove();
        }

    }
}
