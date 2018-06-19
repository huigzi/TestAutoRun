using HPSocketCS;
using InteractiveDataDisplay.WPF;
using MathNet.Numerics.Data.Matlab;
using MathNet.Numerics.LinearAlgebra;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace TestAutoRun
{
    public class FilePath : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private string _path;

        public string Path
        {
       
            get => _path;
            set
            {
                _path = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Path"));
            }

        }

    }

    public enum DataState
    {
        Started, Stoped
    }

    /// <summary>
    /// 程序主框架
    /// </summary>
    public partial class MainWindow : Window
    {
        private FilePath _filePath;
        private DataState _dataState;
        private byte[] _channelInfo;
        private IntPtr _connectId;
        private string _filename;
        private FileStream _filestream;
        private BinaryWriter _sw;
        private long _count;
        private long _count2;
        private int _itemcount = 1;
        private int _samFreq;

        private readonly byte[] _startCommand;
        private readonly byte[] _stopCommand;
        private readonly byte[] _paramReadyCommand;

        private readonly Algorithm _algorithm;
        private readonly List<LineGraph> _lineGraphs;

        private readonly TcpPackServer _server;

        public ObservableCollection<IntPtr> ClinetList;

        private delegate void ShowMsg();

        private delegate void UpdateBytesDelegate(byte[] data);

        private delegate void UpdatePlot(byte[] xData);

        public MainWindow()
        {

            _startCommand = new byte[]
            {
                159, 25, 70, 136, 237, 238, 39, 247, 233, 118, 193, 35, 101, 218, 188, 227, 155, 9, 198, 217, 157, 175,
                171, 90, 149, 245, 251, 8
            };

            _stopCommand = new byte[]
            {
                175, 80, 237, 9, 109, 94, 188, 195, 46, 119, 108, 156, 170, 180, 66, 161, 154, 39, 28, 116, 222, 79,
                135, 52, 171, 58, 114, 157
            };

            _paramReadyCommand = new byte[]
            {
                85, 41, 149, 63, 25, 104, 210, 41, 52, 24, 197, 252, 112, 33, 62, 221, 16, 15, 66, 114, 172, 66, 178,
                142, 11, 42, 150, 130
            };

            _channelInfo = new byte[16];
            _dataState = DataState.Stoped;

            _filePath = new FilePath();
            _algorithm = new Algorithm();
            _server = new TcpPackServer();
            ClinetList = new ObservableCollection<IntPtr>();

            InitializeComponent();

            PathContext.SetBinding(System.Windows.Controls.TextBox.TextProperty, new System.Windows.Data.Binding("Path") { Source = _filePath });

            _lineGraphs = new List<LineGraph>
            {
                new LineGraph(){Stroke = new SolidColorBrush(Color.FromArgb(255, 0, 0, 0)), Description = $"信号通道 {1}", StrokeThickness = 2},
                new LineGraph(){Stroke = new SolidColorBrush(Color.FromArgb(255, 0, 0, 0)), Description = $"信号通道 {1}", StrokeThickness = 2},
                new LineGraph(){Stroke = new SolidColorBrush(Color.FromArgb(255, 255, 0, 0)), Description = $"信号通道 {2}", StrokeThickness = 2},
                new LineGraph(){Stroke = new SolidColorBrush(Color.FromArgb(255, 255, 0, 0)), Description = $"信号通道 {2}", StrokeThickness = 2},
                new LineGraph(){Stroke = new SolidColorBrush(Color.FromArgb(255, 0, 255, 0)), Description = $"信号通道 {3}", StrokeThickness = 2},
                new LineGraph(){Stroke = new SolidColorBrush(Color.FromArgb(255, 0, 255, 0)), Description = $"信号通道 {3}", StrokeThickness = 2},
                new LineGraph(){Stroke = new SolidColorBrush(Color.FromArgb(255, 0, 0, 255)), Description = $"信号通道 {4}", StrokeThickness = 2},
                new LineGraph(){Stroke = new SolidColorBrush(Color.FromArgb(255, 0, 0, 255)), Description = $"信号通道 {4}", StrokeThickness = 2},
            };

        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Window_Loaded(object sender, EventArgs e)
        {
            try
            {
                AddStatus("服务未启动", Color.FromRgb(255, 0, 0));

                IpAddress.Text = Properties.Settings.Default.address;
                ProtNum.Text = Properties.Settings.Default.port;
                Duration.Text = Properties.Settings.Default.duration;
                ChannelNum.Text = Properties.Settings.Default.channel;
                Slider.Value = Properties.Settings.Default.range;
                Min.Text = Properties.Settings.Default.minfreq;
                FreqText.Text = Properties.Settings.Default.freq;
                Cmbox.SelectedIndex = Properties.Settings.Default.sample;
                FreqCombox.SelectedIndex = Properties.Settings.Default.samplerate;
                PathContext.Text = Properties.Settings.Default.filepath;
                _filePath.Path = Properties.Settings.Default.filepath;

                _server.OnPrepareListen += OnPrepareListen;
                _server.OnAccept += OnAccept;
                _server.OnSend += OnSend;
                _server.OnReceive += OnReceive;
                _server.OnClose += OnClose;
                _server.OnShutdown += OnShutdown;

                _channelInfo[0] = 1;

                for (int i = 0; i < 8; i = i + 2)
                {
                    Tlines.Children.Add(_lineGraphs[i]);
                    Flines.Children.Add(_lineGraphs[i + 1]);
                }

                // 设置包头标识,与对端设置保证一致性 存储方式为小端存储 例如 需要传递3个字节 若验证标识为 0xff 则网络助手应写包头为 03 00 C0 3F
                _server.PackHeaderFlag = 0xff;
                // 设置最大封包大小
                _server.MaxPackSize = 0x8FFF;

                AddMsg($"HP-Socket Version: {_server.Version}");
            }
            catch(Exception ex)
            {
                AddMsg(ex.Message);
            }
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            _server.Destroy();

            Properties.Settings.Default.address = IpAddress.Text;
            Properties.Settings.Default.port = ProtNum.Text;
            Properties.Settings.Default.duration = Duration.Text;
            Properties.Settings.Default.channel = ChannelNum.Text;
            Properties.Settings.Default.range = Slider.Value;
            Properties.Settings.Default.minfreq = Min.Text;
            Properties.Settings.Default.freq = FreqText.Text;
            Properties.Settings.Default.sample = Cmbox.SelectedIndex;
            Properties.Settings.Default.samplerate = FreqCombox.SelectedIndex;
            Properties.Settings.Default.filepath = PathContext.Text;
            Properties.Settings.Default.Save();
        }

       

        private void SetDataState(DataState state)
        {
            _dataState = state;
        }

        private HandleResult OnPrepareListen(IntPtr soListen)
        {
            // 监听事件到达了,一般没什么用吧?

            return HandleResult.Ok;
        }

        private HandleResult OnAccept(IntPtr connId, IntPtr pClient)
        {
            // 客户进入了

            // 获取客户端ip和端口
            string ip = string.Empty;
            ushort port = 0;
            if (_server.GetRemoteAddress(connId, ref ip, ref port))
            {
                AddMsg($" > [{connId},OnAccept] -> PASS({ip}:{port})");
                AddClient($"{ip}:{port}");
            }
            else
            {
                AddMsg($" > [{connId},OnAccept] -> Server_GetClientAddress() Error");
            }

            _connectId = connId;

            // 设置附加数据
            ClientInfo clientInfo = new ClientInfo
            {
                ConnId = connId,
                IpAddress = ip,
                Port = port
            };

            if (_server.SetExtra(connId, clientInfo) == false)
            {
                AddMsg($" > [{connId},OnAccept] -> SetConnectionExtra fail");
            }

            ClinetList.Add(connId);

            AddStatus("下位机已连接", Color.FromRgb(0, 0, 255));

            return HandleResult.Ok;
        }

        private HandleResult OnSend(IntPtr connId, byte[] bytes)
        {
            // 服务器发数据了

            AddMsg($" > [{connId},OnSend] -> ({bytes.Length} bytes)");

            return HandleResult.Ok;
        }

        private HandleResult OnReceive(IntPtr connId, byte[] bytes)
        {
            // 数据到达了
            try
            {
                // 获取附加数据
                var clientInfo = _server.GetExtra<ClientInfo>(connId);
                if (clientInfo != null)
                {
                    // clientInfo 就是accept里传入的附加数据了
                    AddMsg($" > [{clientInfo.ConnId},OnReceive] -> {clientInfo.IpAddress}:{clientInfo.Port} ({bytes.Length} bytes)");
                }
                else
                {
                    AddMsg($" > [{connId},OnReceive] -> ({bytes.Length} bytes)");
                    return HandleResult.Error;
                }

                if (_dataState == DataState.Started)
                {
                    Dispatcher.Invoke(new UpdateBytesDelegate(SaveData), bytes);
                }

                if (bytes.Length > 32)
                {
                    Dispatcher.Invoke(new UpdatePlot(AddPlot), bytes);
                }
                else if(bytes.Length <= 32 && bytes.Length > 10)
                {
                    if (bytes.SequenceEqual(_startCommand))
                    {
                        SetDataState(DataState.Started);
                    }

                    if (bytes.SequenceEqual(_stopCommand))
                    {
                        _sw.Dispose();
                        _filestream.Dispose();
                        SetDataState(DataState.Stoped);
                        Dispatcher.Invoke(() =>
                        {
                            BtnSend2.IsEnabled = true;
                            BtnSend5.IsEnabled = true;
                            FreqChange.IsEnabled = true;
                            RuntimeButton.IsEnabled = true;

                        });
                    }

                    if (bytes.SequenceEqual(_paramReadyCommand))
                    {
                        Dispatcher.Invoke(() =>
                        {
                            BtnSend2.IsEnabled = true;
                            BtnSend5.IsEnabled = true;
                            BtnSend6.IsEnabled = true;
                            RuntimeButton.IsEnabled = true;
                            FreqChange.IsEnabled = true;

                        });
                    }
                }
                else
                {
                    if(bytes[0] == 0x00)
                    {
                        Dispatcher.BeginInvoke(DispatcherPriority.Normal, (ShowMsg)delegate
                        {
                            _samFreq = 1000;
                            SampleContent.Content = "1000Hz";
                        });                    
                    }
                    else if(bytes[0] == 0x01)
                    {
                        Dispatcher.BeginInvoke(DispatcherPriority.Normal, (ShowMsg)delegate
                        {
                            _samFreq = 2000;
                            SampleContent.Content = "2000Hz";
                        });
                    }
                    else if(bytes[0] == 0x02)
                    {
                        Dispatcher.BeginInvoke(DispatcherPriority.Normal, (ShowMsg)delegate
                        {
                            _samFreq = 4000;
                            SampleContent.Content = "4000Hz";
                        });
                    }
                    else
                    {
                        Dispatcher.BeginInvoke(DispatcherPriority.Normal, (ShowMsg)delegate
                        {
                            _samFreq = 8000;
                            SampleContent.Content = "8000Hz";
                        });
                    }

                    AddStatus("工作模式", Color.FromRgb(0, 255, 0));
                }

                return HandleResult.Ok;
            }
            catch (Exception)
            {

                return HandleResult.Error;
            }
        }

        private HandleResult OnClose(IntPtr connId, SocketOperation enOperation, int errorCode)
        {
            AddMsg(errorCode == 0
                ? $" > [{connId},OnClose]"
                : $" > [{connId},OnError] -> OP:{enOperation},CODE:{errorCode}");

            var index = ClinetList.IndexOf(connId);
            ClinetList.RemoveAt(index);
            RemoveClient(index);

            if (_server.RemoveExtra(connId) == false)
            {
                AddMsg($" > [{connId},OnClose] -> SetConnectionExtra({connId}, null) fail");
            }

            AddStatus("下位机已断开", Color.FromRgb(255, 0, 0));

            return HandleResult.Ok;
        }

        private HandleResult OnShutdown()
        {
            // 服务关闭了
            AddStatus("服务未启动", Color.FromRgb(255, 0, 0));
            AddMsg("已停止服务");
            return HandleResult.Ok;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="msg"></param>

        private void AddMsg(string msg)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Normal, (ShowMsg)delegate
            {
                if (LbxMsg.Items.Count > 50)
                {
                    LbxMsg.Items.RemoveAt(0);
                }
                LbxMsg.Items.Add(msg);
            });
        }

        private void AddStatus(string msg, Color color)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Normal, (ShowMsg)delegate
            {
                WorkStateContext.Content = msg;
                WorkStateContext.Foreground = new SolidColorBrush(color);
            });
        }

        private void AddPlot(byte[] xData)
        {          
            int j = 0;

            for (int i = 0; i < 16; i++)
            {
                if (_channelInfo[i] == 1)
                {
                    _algorithm.CalculateGraph(xData, i, 512);
                    _lineGraphs[j * 2].Description = $"信号通道 {i + 1}";
                    _lineGraphs[j * 2 + 1].Description = $"信号通道 {i + 1}";
                    _lineGraphs[j * 2].PlotY(_algorithm.TimerGraph);
                    _lineGraphs[j * 2 + 1].Plot(_algorithm.Xaxis, _algorithm.Finresult);
                    j++;
                }
            }
        }

        private void AddClient(string msg)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Normal, (ShowMsg)delegate
            {
                NetMsg.Content = msg;
            });
        }

        private void RemoveClient(int index)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Normal, (ShowMsg)delegate
            {
                NetMsg.Content = null;   
            });
        }

        private void SaveData(byte[] bytes)
        {
            if (string.IsNullOrEmpty(_filePath.Path)) return;
            _sw.Write(bytes);
            _sw.Flush();

        }

        private void Connectbutton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(IpAddress.Text) || string.IsNullOrEmpty(ProtNum.Text))
            {
                AddMsg("请填写完整目标端地址及端口号");
                Connectbutton.IsChecked = false;
                return;
            }
            if (Connectbutton.IsChecked != null && (bool)Connectbutton.IsChecked)
            {
                string tmpIp = IpAddress.Text.Trim();
                ushort port = ushort.Parse(ProtNum.Text.Trim());

                _server.IpAddress = tmpIp;
                _server.Port = port;

                try
                {
                    if(_server.Start())
                    {
                        AddMsg($"服务已经正常启动 ->({tmpIp}:{port})");
                        AddStatus("下位机未连接", Color.FromRgb(255, 0, 0));
                    }
                    else
                    {
   
                        Connectbutton.IsChecked = false;
                    }
                }
                catch (Exception ex)
                {
                    AddMsg(ex.Message);
                }
            }
            else
            {

                if (_server.Stop())
                {
                }
                else
                {
                    AddMsg($"停止错误 -> {_server.ErrorMessage}({_server.ErrorCode})");
                }

            }
        }

        private void Runtime_Click(object sender, RoutedEventArgs e)
        {
            if (RuntimeButton.IsChecked != null && (bool)RuntimeButton.IsChecked)
            {
                var commandBuf = new byte[16];

                commandBuf[0] = 0x01;
                commandBuf[1] = 0x01;
                commandBuf[2] = 0x04;
                commandBuf[3] = 0x01;
                _server.Send(_connectId, commandBuf, 4);

                AddStatus("调试模式", Color.FromRgb(0, 255, 0));

            }
            else
            {
                var commandBuf = new byte[16];

                commandBuf[0] = 0x01;
                commandBuf[1] = 0x01;
                commandBuf[2] = 0x04;
                commandBuf[3] = 0x00;
                _server.Send(_connectId, commandBuf, 4);

                AddStatus("工作模式", Color.FromRgb(0, 255, 0));
            }
        }

        private void BtnSend2_Click(object sender, RoutedEventArgs e)
        {
           
            var btn = (System.Windows.Controls.Button)sender;

            if (string.IsNullOrEmpty(_filePath.Path))
            {
                var mDialog = new FolderBrowserDialog();
                DialogResult result = mDialog.ShowDialog();

                if (result == System.Windows.Forms.DialogResult.Cancel)
                {
                    return;
                }
                _filePath.Path = mDialog.SelectedPath.Trim();
            }

            if (_dataState == DataState.Started)
            {
                BtnSend5.IsEnabled = true;
                SetDataState(DataState.Stoped);
                btn.Content = "开始保存";
                _sw.Dispose();
                _filestream.Dispose();

            }
            else
            {
                BtnSend5.IsEnabled = false;
                _count++;
                _filename = DateTime.Now.ToString("MM-dd") + "第" + Convert.ToString(_count) + "次非辨识数据";
                SetDataState(DataState.Started);
                btn.Content = "停止保存";
                string path = _filePath.Path + "\\" + _filename;
                _filestream = new FileStream(path: path, mode: FileMode.Create, access: FileAccess.Write);
                _sw = new BinaryWriter(_filestream);

            }

        }

        private void BtnSend3_Click(object sender, RoutedEventArgs e)
        {

        }

        private void BtnSend5_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(Duration.Text) || string.IsNullOrEmpty(ChannelNum.Text) || string.IsNullOrEmpty(Range.Content.ToString()))
            {
                return;
            }

            if (Duration.Text != "0" && ChannelNum.Text != "0" && Range.Content.ToString() !="0")
            {

                if (string.IsNullOrEmpty(_filePath.Path))
                {
                    var mDialog = new FolderBrowserDialog();
                    DialogResult result = mDialog.ShowDialog();

                    if (result == System.Windows.Forms.DialogResult.Cancel)
                    {
                        return;
                    }
                    _filePath.Path = mDialog.SelectedPath.Trim();
                }

                BtnSend2.IsEnabled = false;
                BtnSend5.IsEnabled = false;
                FreqChange.IsEnabled = false;
                RuntimeButton.IsEnabled = false;

                _count2++;

                _filename = DateTime.Now.ToString("MM-dd") + "第" + Convert.ToString(_count2) + "次辨识数据（"  + ChannelNum.Text +
                           "）通道";
                string path = _filePath.Path + "\\" + _filename;
                
                _filestream = new FileStream(path: path, mode: FileMode.Create, access: FileAccess.Write);
                _sw = new BinaryWriter(_filestream);

                double[] x = new double[1024];

                if (Cmbox.SelectedIndex == 0)
                {
                    Random ra = new Random(unchecked((int)DateTime.Now.Ticks));
                    for (int i = 0; i < x.Length; i++)
                    {
                        x[i] = ra.Next(0, 65535);
                    }

                    var commandBuf = new byte[16];

                    commandBuf[0] = 0x01;
                    commandBuf[1] = 0x01;
                    commandBuf[2] = 0x02;
                    commandBuf[3] = 0x00;
                    commandBuf[4] = byte.Parse(Duration.Text.Trim());
                    if (commandBuf[4] > 120)
                    {
                        commandBuf[4] = 120;
                    }

                    commandBuf[5] = byte.Parse(ChannelNum.Text.Trim());
                    commandBuf[5] -= 1;

                    float temp2 = float.Parse(Range.Content.ToString().Trim());
                    temp2 = temp2 * 10;
                    commandBuf[6] = (byte)temp2;

                    _server.Send(_connectId, commandBuf, 7);
                }
                else if (Cmbox.SelectedIndex == 1)
                {
                    uint frequency = 800;

                    for (int i = 0; i < x.Length; i++)
                    {
                        x[i] = Math.Sin(2 * 3.14 * frequency * i / 25000.0);
                    }

                    var commandBuf = new byte[16];

                    commandBuf[0] = 0x01;
                    commandBuf[1] = 0x01;
                    commandBuf[2] = 0x02;
                    commandBuf[3] = 0x01;
                    commandBuf[4] = byte.Parse(Duration.Text.Trim());
                    if (commandBuf[4] > 120)
                    {
                        commandBuf[4] = 120;
                    }
                    commandBuf[5] = byte.Parse(ChannelNum.Text.Trim());
                    commandBuf[5] -= 1;

                    float temp2 = float.Parse(Range.Content.ToString().Trim());
                    temp2 = temp2 * 10;
                    commandBuf[6] = (byte) temp2;

                    float temp3 = float.Parse(FreqText.Text.Trim());
                    temp3 = temp3 / 100;
                    commandBuf[7] = (byte) temp3;

                    _server.Send(_connectId, commandBuf, 8);
                }
                else
                {
                    for (int i = 0; i < x.Length; i++)
                    {
                        double tempPhase = 3.14 * 2 * (Math.Pow(2, (i / (double)350)) - 1) / Math.Log10(2);
                        x[i] = Math.Sin(tempPhase);
                    }

                    var commandBuf = new byte[16];

                    commandBuf[0] = 0x01;
                    commandBuf[1] = 0x01;
                    commandBuf[2] = 0x02;
                    commandBuf[3] = 0x02;
                    commandBuf[4] = byte.Parse(Duration.Text.Trim());
                    if (commandBuf[4] > 120)
                    {
                        commandBuf[4] = 120;
                    }
                    commandBuf[5] = byte.Parse(ChannelNum.Text.Trim());
                    commandBuf[5] -= 1;

                    float temp2 = float.Parse(Range.Content.ToString().Trim());
                    temp2 = temp2 * 10;
                    commandBuf[6] = (byte) temp2;

                    int temp = int.Parse(Min.Text.Trim());
                    temp = temp / 100;
                    commandBuf[7] = (byte) temp;

                    temp = int.Parse(Max.Text.Trim());
                    temp = temp / 100;
                    commandBuf[8] = (byte) temp;

                    _server.Send(_connectId, commandBuf, 9);
                }

                var tlg = new LineGraph();
                T2Lines.Children.Clear();
                T2Lines.Children.Add(tlg);

                tlg.Stroke = new SolidColorBrush(Color.FromArgb(255, 0, 10, 0));
                tlg.Description = "待发送信号";
                tlg.StrokeThickness = 2;
                tlg.PlotY(x);
            }
            else
            {
                AddMsg($"参数错误 -> 参数设置错误，部分必要参数为0");
            }
        }

        private void BtnSend6_Click(object sender, RoutedEventArgs e)
        {
            BtnSend2.IsEnabled = false;
            BtnSend5.IsEnabled = false;
            BtnSend6.IsEnabled = false;
            RuntimeButton.IsEnabled = false;
            FreqChange.IsEnabled = false;

            var openFileDialog = new Microsoft.Win32.OpenFileDialog()
            {
                Filter = ""
            };

            var result = openFileDialog.ShowDialog();

            if (result == false)
            {

                BtnSend2.IsEnabled = true;
                BtnSend5.IsEnabled = true;
                BtnSend6.IsEnabled = true;
                RuntimeButton.IsEnabled = true;
                FreqChange.IsEnabled = true;
                return;
            }

            var commandBuf = new byte[16];

            commandBuf[0] = 0x01;
            commandBuf[1] = 0x01;
            commandBuf[2] = 0x01;
            commandBuf[3] = 0x00;

            _server.Send(_connectId, commandBuf, 4);

            try
            {

                var m1 = MatlabReader.ReadAll<float>(filePath: openFileDialog.FileName);

                if (m1.ContainsKey("param"))
                {
                    if (m1["param"].ColumnCount > 16)
                    {
                        AddMsg($"读取错误 -> 参数个数错误，请检查文件");

                        return;

                    }

                    var paramBuf = new byte[2048];

                    for (int i = 0; i < m1["param"].ColumnCount; i++)
                    {
                        var temp = BitConverter.GetBytes(m1["param"][0, i]);
                        paramBuf[4 * i] = temp[0];
                        paramBuf[4 * i + 1] = temp[1];
                        paramBuf[4 * i + 2] = temp[2];
                        paramBuf[4 * i + 3] = temp[3];
                    }

                    _server.Send(_connectId, paramBuf, 64);
                }

                if (m1.ContainsKey("filterCoeff"))
                {
                    if (m1["filterCoeff"].ColumnCount > 512 || m1["filterCoeff"].RowCount > 16)
                    {
                        AddMsg($"读取错误 -> 滤波器系数错误，请检查文件");

                        return;

                    }

                    for (int i = 0; i < m1["filterCoeff"].RowCount; i++)
                    {

                        var paramBuf = new byte[2048];

                        for (int j = 0; j < m1["filterCoeff"].ColumnCount; j++)
                        {
                            var temp = BitConverter.GetBytes(m1["filterCoeff"][i, j]);
                            paramBuf[4 * j] = temp[0];
                            paramBuf[4 * j + 1] = temp[1];
                            paramBuf[4 * j + 2] = temp[2];
                            paramBuf[4 * j + 3] = temp[3];
                        }

                        _server.Send(_connectId, paramBuf, 2048);
                    }
                }

                //foreach (var param in m1)
                //{
                //    for (int i = 0; i < param.Value.RowCount; i++)
                //    {
                //        for (int j = 0; j < param.Value.ColumnCount; j++)
                //        {
                //            var temp = BitConverter.GetBytes(param.Value[i, j]);
                //            _paramBuf[4 * j] = temp[0];
                //            _paramBuf[4 * j + 1] = temp[1];
                //            _paramBuf[4 * j + 2] = temp[2];
                //            _paramBuf[4 * j + 3] = temp[3];
                //        }

                //        _server.Send(_connectId, _paramBuf, param.Value.ColumnCount * 4);
                //    }
                //}
            }

            catch
            {
                AddMsg($"读取错误 -> 更改参数错误，请联系开发商");
            }
        }

        private void BtnSend9_Click(object sender, RoutedEventArgs e)
        {
            for (int i = 0; i < _channelInfo.Length; i++)
            {
                _channelInfo[i] = 0;
            }

            for (int i = 0; i < ChannelList.Children.Count; i++)
            {
                var item = ChannelList.Children[i];
                if (item is System.Windows.Controls.CheckBox checkBoxItem)
                {
                    checkBoxItem.IsChecked = false;
                    checkBoxItem.IsEnabled = true;
                }
            }
        }

        private void BtnSend1_Click(object sender, RoutedEventArgs e)
        {
            //var openFileDialog = new Microsoft.Win32.OpenFileDialog()
            //{
            //    Filter = "二进制文件|*"
            //};
            //var result = openFileDialog.ShowDialog();
            //if (result == false)
            //{
            //    return;
            //}
            //try
            //{
            //    FileStream paramRead = new FileStream(@openFileDialog.FileName, FileMode.Open);
            //    int count = paramRead.Read(_paramBuf, 0, 36876);
            //    paramRead.Dispose();
            //}
            //catch
            //{

            //    System.Windows.MessageBox.Show("打开文件错误，请确认文件是否存在或损坏");
            //}
        }

        private void CheckBox1_Click(object sender, RoutedEventArgs e)
        {
            if (CheckBox1.IsChecked == true)
            {
                _channelInfo[0] = 1;

                _itemcount++;
                _lineGraphs[_itemcount * 2 - 1].Visibility = Visibility.Visible;
                _lineGraphs[_itemcount * 2 - 2].Visibility = Visibility.Visible;

                if (_itemcount > 3)
                {
                    for (int i = 0; i < ChannelList.Children.Count; i++)
                    {
                        var item = ChannelList.Children[i];
                        if (item is System.Windows.Controls.CheckBox checkBoxItem)
                        {
                            checkBoxItem.IsEnabled = false;
                        }
                    }
                }

                CheckBox1.IsEnabled = true;
            }

            else
            {
                _channelInfo[0] = 0;


                _lineGraphs[_itemcount * 2 - 1].Visibility = Visibility.Collapsed;
                _lineGraphs[_itemcount * 2 - 2].Visibility = Visibility.Collapsed;
                _itemcount--;
               

                if (_itemcount > 2)
                {
                    for (int i = 0; i < ChannelList.Children.Count; i++)
                    {
                        var item = ChannelList.Children[i];
                        if (item is System.Windows.Controls.CheckBox checkBoxItem)
                        {
                            checkBoxItem.IsEnabled = true;
                        }
                    }
                }

            }
        }

        private void CheckBox2_Click(object sender, RoutedEventArgs e)
        {
            if (CheckBox2.IsChecked == true)
            {
                _channelInfo[1] = 1;

                _itemcount++;
                _lineGraphs[_itemcount * 2 - 1].Visibility = Visibility.Visible;
                _lineGraphs[_itemcount * 2 - 2].Visibility = Visibility.Visible;

                if (_itemcount > 3)
                {
                    for (int i = 0; i < ChannelList.Children.Count; i++)
                    {
                        var item = ChannelList.Children[i];
                        if (item is System.Windows.Controls.CheckBox checkBoxItem)
                        {
                            checkBoxItem.IsEnabled = false;
                        }
                    }
                }

                CheckBox2.IsEnabled = true;

            }
            else
            {
                _channelInfo[1] = 0;

                _lineGraphs[_itemcount * 2 - 1].Visibility = Visibility.Collapsed;
                _lineGraphs[_itemcount * 2 - 2].Visibility = Visibility.Collapsed;
                _itemcount--;

                if (_itemcount > 2)
                {
                    for (int i = 0; i < ChannelList.Children.Count; i++)
                    {
                        var item = ChannelList.Children[i];
                        if (item is System.Windows.Controls.CheckBox checkBoxItem)
                        {
                            checkBoxItem.IsEnabled = true;
                        }
                    }
                }
            }
        }

        private void CheckBox3_Click(object sender, RoutedEventArgs e)
        {
            if (CheckBox3.IsChecked == true)
            {
                _channelInfo[2] = 1;

                _itemcount++;
                _lineGraphs[_itemcount * 2 - 1].Visibility = Visibility.Visible;
                _lineGraphs[_itemcount * 2 - 2].Visibility = Visibility.Visible;

                if (_itemcount > 3)
                {
                    for (int i = 0; i < ChannelList.Children.Count; i++)
                    {
                        var item = ChannelList.Children[i];
                        if (item is System.Windows.Controls.CheckBox checkBoxItem)
                        {
                            checkBoxItem.IsEnabled = false;
                        }
                    }
                }

                CheckBox3.IsEnabled = true;
            }
            else
            {
                _channelInfo[2] = 0;

                _lineGraphs[_itemcount * 2 - 1].Visibility = Visibility.Collapsed;
                _lineGraphs[_itemcount * 2 - 2].Visibility = Visibility.Collapsed;
                _itemcount--;

                if (_itemcount > 2)
                {
                    for (int i = 0; i < ChannelList.Children.Count; i++)
                    {
                        var item = ChannelList.Children[i];
                        if (item is System.Windows.Controls.CheckBox checkBoxItem)
                        {
                            checkBoxItem.IsEnabled = true;
                        }
                    }
                }
            }
        }

        private void CheckBox4_Click(object sender, RoutedEventArgs e)
        {
            if (CheckBox4.IsChecked == true)
            {
                _channelInfo[3] = 1;

                _itemcount++;
                _lineGraphs[_itemcount * 2 - 1].Visibility = Visibility.Visible;
                _lineGraphs[_itemcount * 2 - 2].Visibility = Visibility.Visible;

                if (_itemcount > 3)
                {
                    for (int i = 0; i < ChannelList.Children.Count; i++)
                    {
                        var item = ChannelList.Children[i];
                        if (item is System.Windows.Controls.CheckBox checkBoxItem)
                        {
                            checkBoxItem.IsEnabled = false;
                        }
                    }
                }

                CheckBox4.IsEnabled = true;
            }
            else
            {
                _channelInfo[3] = 0;

                _lineGraphs[_itemcount * 2 - 1].Visibility = Visibility.Collapsed;
                _lineGraphs[_itemcount * 2 - 2].Visibility = Visibility.Collapsed;
                _itemcount--;


                if (_itemcount > 2)
                {
                    for (int i = 0; i < ChannelList.Children.Count; i++)
                    {
                        var item = ChannelList.Children[i];
                        if (item is System.Windows.Controls.CheckBox checkBoxItem)
                        {
                            checkBoxItem.IsEnabled = true;
                        }
                    }
                }
            }
        }

        private void CheckBox5_Click(object sender, RoutedEventArgs e)
        {
            if (CheckBox5.IsChecked == true)
            {
                _channelInfo[4] = 1;

                _itemcount++;
                _lineGraphs[_itemcount * 2 - 1].Visibility = Visibility.Visible;
                _lineGraphs[_itemcount * 2 - 2].Visibility = Visibility.Visible;

                if (_itemcount > 3)
                {
                    for (int i = 0; i < ChannelList.Children.Count; i++)
                    {
                        var item = ChannelList.Children[i];
                        if (item is System.Windows.Controls.CheckBox checkBoxItem)
                        {
                            checkBoxItem.IsEnabled = false;
                        }
                    }
                }

                CheckBox5.IsEnabled = true;

            }
            else
            {
                _channelInfo[4] = 0;

                _lineGraphs[_itemcount * 2 - 1].Visibility = Visibility.Collapsed;
                _lineGraphs[_itemcount * 2 - 2].Visibility = Visibility.Collapsed;
                _itemcount--;

                if (_itemcount > 2)
                {
                    for (int i = 0; i < ChannelList.Children.Count; i++)
                    {
                        var item = ChannelList.Children[i];
                        if (item is System.Windows.Controls.CheckBox checkBoxItem)
                        {
                            checkBoxItem.IsEnabled = true;
                        }
                    }
                }
            }
        }

        private void CheckBox6_Click(object sender, RoutedEventArgs e)
        {
            if (CheckBox6.IsChecked == true)
            {
                _channelInfo[5] = 1;

                _itemcount++;
                _lineGraphs[_itemcount * 2 - 1].Visibility = Visibility.Visible;
                _lineGraphs[_itemcount * 2 - 2].Visibility = Visibility.Visible;

                if (_itemcount > 3)
                {
                    for (int i = 0; i < ChannelList.Children.Count; i++)
                    {
                        var item = ChannelList.Children[i];
                        if (item is System.Windows.Controls.CheckBox checkBoxItem)
                        {
                            checkBoxItem.IsEnabled = false;
                        }
                    }
                }

                CheckBox6.IsEnabled = true;
            }
            else
            {
                _channelInfo[5] = 0;

                _lineGraphs[_itemcount * 2 - 1].Visibility = Visibility.Collapsed;
                _lineGraphs[_itemcount * 2 - 2].Visibility = Visibility.Collapsed;
                _itemcount--;

                if (_itemcount > 2)
                {
                    for (int i = 0; i < ChannelList.Children.Count; i++)
                    {
                        var item = ChannelList.Children[i];
                        if (item is System.Windows.Controls.CheckBox checkBoxItem)
                        {
                            checkBoxItem.IsEnabled = true;
                        }
                    }
                }
            }
        }

        private void CheckBox7_Click(object sender, RoutedEventArgs e)
        {
            if (CheckBox7.IsChecked == true)
            {
                _channelInfo[6] = 1;

                _itemcount++;
                _lineGraphs[_itemcount * 2 - 1].Visibility = Visibility.Visible;
                _lineGraphs[_itemcount * 2 - 2].Visibility = Visibility.Visible;

                if (_itemcount > 3)
                {
                    for (int i = 0; i < ChannelList.Children.Count; i++)
                    {
                        var item = ChannelList.Children[i];
                        if (item is System.Windows.Controls.CheckBox checkBoxItem)
                        {
                            checkBoxItem.IsEnabled = false;
                        }
                    }
                }

                CheckBox7.IsEnabled = true;
            }
            else
            {
                _channelInfo[6] = 0;

                _lineGraphs[_itemcount * 2 - 1].Visibility = Visibility.Collapsed;
                _lineGraphs[_itemcount * 2 - 2].Visibility = Visibility.Collapsed;
                _itemcount--;

                if (_itemcount > 2)
                {
                    for (int i = 0; i < ChannelList.Children.Count; i++)
                    {
                        var item = ChannelList.Children[i];
                        if (item is System.Windows.Controls.CheckBox checkBoxItem)
                        {
                            checkBoxItem.IsEnabled = true;
                        }
                    }
                }
            }
        }

        private void CheckBox8_Click(object sender, RoutedEventArgs e)
        {
            if (CheckBox8.IsChecked == true)
            {
                _channelInfo[7] = 1;

                _itemcount++;
                _lineGraphs[_itemcount * 2 - 1].Visibility = Visibility.Visible;
                _lineGraphs[_itemcount * 2 - 2].Visibility = Visibility.Visible;

                if (_itemcount > 3)
                {
                    for (int i = 0; i < ChannelList.Children.Count; i++)
                    {
                        var item = ChannelList.Children[i];
                        if (item is System.Windows.Controls.CheckBox checkBoxItem)
                        {
                            checkBoxItem.IsEnabled = false;
                        }
                    }
                }

                CheckBox8.IsEnabled = true;
            }
            else
            {
                _channelInfo[7] = 0;

                _lineGraphs[_itemcount * 2 - 1].Visibility = Visibility.Collapsed;
                _lineGraphs[_itemcount * 2 - 2].Visibility = Visibility.Collapsed;
                _itemcount--;

                if (_itemcount > 2)
                {
                    for (int i = 0; i < ChannelList.Children.Count; i++)
                    {
                        var item = ChannelList.Children[i];
                        if (item is System.Windows.Controls.CheckBox checkBoxItem)
                        {
                            checkBoxItem.IsEnabled = true;
                        }
                    }
                }
            }
        }

        private void CheckBox9_Click(object sender, RoutedEventArgs e)
        {
            if (CheckBox9.IsChecked == true)
            {
                _channelInfo[8] = 1;

                _itemcount++;
                _lineGraphs[_itemcount * 2 - 1].Visibility = Visibility.Visible;
                _lineGraphs[_itemcount * 2 - 2].Visibility = Visibility.Visible;

                if (_itemcount > 3)
                {
                    for (int i = 0; i < ChannelList.Children.Count; i++)
                    {
                        var item = ChannelList.Children[i];
                        if (item is System.Windows.Controls.CheckBox checkBoxItem)
                        {
                            checkBoxItem.IsEnabled = false;
                        }
                    }
                }

                CheckBox9.IsEnabled = true;
            }
            else
            {
                _channelInfo[8] = 0;

                _lineGraphs[_itemcount * 2 - 1].Visibility = Visibility.Collapsed;
                _lineGraphs[_itemcount * 2 - 2].Visibility = Visibility.Collapsed;
                _itemcount--;

                if (_itemcount > 2)
                {
                    for (int i = 0; i < ChannelList.Children.Count; i++)
                    {
                        var item = ChannelList.Children[i];
                        if (item is System.Windows.Controls.CheckBox checkBoxItem)
                        {
                            checkBoxItem.IsEnabled = true;
                        }
                    }
                }
            }
        }

        private void CheckBox10_Click(object sender, RoutedEventArgs e)
        {
            if (CheckBox10.IsChecked == true)
            {
                _channelInfo[9] = 1;

                _itemcount++;
                _lineGraphs[_itemcount * 2 - 1].Visibility = Visibility.Visible;
                _lineGraphs[_itemcount * 2 - 2].Visibility = Visibility.Visible;

                if (_itemcount > 3)
                {
                    for (int i = 0; i < ChannelList.Children.Count; i++)
                    {
                        var item = ChannelList.Children[i];
                        if (item is System.Windows.Controls.CheckBox checkBoxItem)
                        {
                            checkBoxItem.IsEnabled = false;
                        }
                    }
                }

                CheckBox10.IsEnabled = true;
            }
            else
            {
                _channelInfo[9] = 0;

                _lineGraphs[_itemcount * 2 - 1].Visibility = Visibility.Collapsed;
                _lineGraphs[_itemcount * 2 - 2].Visibility = Visibility.Collapsed;
                _itemcount--;

                if (_itemcount > 2)
                {
                    for (int i = 0; i < ChannelList.Children.Count; i++)
                    {
                        var item = ChannelList.Children[i];
                        if (item is System.Windows.Controls.CheckBox checkBoxItem)
                        {
                            checkBoxItem.IsEnabled = true;
                        }
                    }
                }
            }
        }

        private void CheckBox11_Click(object sender, RoutedEventArgs e)
        {
            if (CheckBox11.IsChecked == true)
            {
                _channelInfo[10] = 1;

                _itemcount++;
                _lineGraphs[_itemcount * 2 - 1].Visibility = Visibility.Visible;
                _lineGraphs[_itemcount * 2 - 2].Visibility = Visibility.Visible;

                if (_itemcount > 3)
                {
                    for (int i = 0; i < ChannelList.Children.Count; i++)
                    {
                        var item = ChannelList.Children[i];
                        if (item is System.Windows.Controls.CheckBox checkBoxItem)
                        {
                            checkBoxItem.IsEnabled = false;
                        }
                    }
                }

                CheckBox11.IsEnabled = true;
            }
            else
            {
                _channelInfo[10] = 0;

                _lineGraphs[_itemcount * 2 - 1].Visibility = Visibility.Collapsed;
                _lineGraphs[_itemcount * 2 - 2].Visibility = Visibility.Collapsed;
                _itemcount--;

                if (_itemcount > 2)
                {
                    for (int i = 0; i < ChannelList.Children.Count; i++)
                    {
                        var item = ChannelList.Children[i];
                        if (item is System.Windows.Controls.CheckBox checkBoxItem)
                        {
                            checkBoxItem.IsEnabled = true;
                        }
                    }
                }
            }
        }

        private void CheckBox12_Click(object sender, RoutedEventArgs e)
        {
            if (CheckBox12.IsChecked == true)
            {
                _channelInfo[11] = 1;

                _itemcount++;
                _lineGraphs[_itemcount * 2 - 1].Visibility = Visibility.Visible;
                _lineGraphs[_itemcount * 2 - 2].Visibility = Visibility.Visible;

                if (_itemcount > 3)
                {
                    for (int i = 0; i < ChannelList.Children.Count; i++)
                    {
                        var item = ChannelList.Children[i];
                        if (item is System.Windows.Controls.CheckBox checkBoxItem)
                        {
                            checkBoxItem.IsEnabled = false;
                        }
                    }
                }

                CheckBox12.IsEnabled = true;
            }
            else
            {
                _channelInfo[11] = 0;

                _lineGraphs[_itemcount * 2 - 1].Visibility = Visibility.Collapsed;
                _lineGraphs[_itemcount * 2 - 2].Visibility = Visibility.Collapsed;
                _itemcount--;

                if (_itemcount > 2)
                {
                    for (int i = 0; i < ChannelList.Children.Count; i++)
                    {
                        var item = ChannelList.Children[i];
                        if (item is System.Windows.Controls.CheckBox checkBoxItem)
                        {
                            checkBoxItem.IsEnabled = true;
                        }
                    }
                }
            }
        }

        private void CheckBox13_Click(object sender, RoutedEventArgs e)
        {
            if (CheckBox13.IsChecked == true)
            {
                _channelInfo[12] = 1;

                _itemcount++;
                _lineGraphs[_itemcount * 2 - 1].Visibility = Visibility.Visible;
                _lineGraphs[_itemcount * 2 - 2].Visibility = Visibility.Visible;

                if (_itemcount > 3)
                {
                    for (int i = 0; i < ChannelList.Children.Count; i++)
                    {
                        var item = ChannelList.Children[i];
                        if (item is System.Windows.Controls.CheckBox checkBoxItem)
                        {
                            checkBoxItem.IsEnabled = false;
                        }
                    }
                }

                CheckBox13.IsEnabled = true;
            }
            else
            {
                _channelInfo[12] = 0;

                _lineGraphs[_itemcount * 2 - 1].Visibility = Visibility.Collapsed;
                _lineGraphs[_itemcount * 2 - 2].Visibility = Visibility.Collapsed;
                _itemcount--;

                if (_itemcount > 2)
                {
                    for (int i = 0; i < ChannelList.Children.Count; i++)
                    {
                        var item = ChannelList.Children[i];
                        if (item is System.Windows.Controls.CheckBox checkBoxItem)
                        {
                            checkBoxItem.IsEnabled = true;
                        }
                    }
                }
            }
        }

        private void CheckBox14_Click(object sender, RoutedEventArgs e)
        {
            if (CheckBox14.IsChecked == true)
            {
                _channelInfo[13] = 1;

                _itemcount++;
                _lineGraphs[_itemcount * 2 - 1].Visibility = Visibility.Visible;
                _lineGraphs[_itemcount * 2 - 2].Visibility = Visibility.Visible;

                if (_itemcount > 3)
                {
                    for (int i = 0; i < ChannelList.Children.Count; i++)
                    {
                        var item = ChannelList.Children[i];
                        if (item is System.Windows.Controls.CheckBox checkBoxItem)
                        {
                            checkBoxItem.IsEnabled = false;
                        }
                    }
                }

                CheckBox14.IsEnabled = true;
            }
            else
            {
                _channelInfo[13] = 0;

                _lineGraphs[_itemcount * 2 - 1].Visibility = Visibility.Collapsed;
                _lineGraphs[_itemcount * 2 - 2].Visibility = Visibility.Collapsed;
                _itemcount--;

                if (_itemcount > 2)
                {
                    for (int i = 0; i < ChannelList.Children.Count; i++)
                    {
                        var item = ChannelList.Children[i];
                        if (item is System.Windows.Controls.CheckBox checkBoxItem)
                        {
                            checkBoxItem.IsEnabled = true;
                        }
                    }
                }
            }
        }

        private void CheckBox15_Click(object sender, RoutedEventArgs e)
        {
            if (CheckBox15.IsChecked == true)
            {
                _channelInfo[14] = 1;

                _itemcount++;
                _lineGraphs[_itemcount * 2 - 1].Visibility = Visibility.Visible;
                _lineGraphs[_itemcount * 2 - 2].Visibility = Visibility.Visible;

                if (_itemcount > 3)
                {
                    for (int i = 0; i < ChannelList.Children.Count; i++)
                    {
                        var item = ChannelList.Children[i];
                        if (item is System.Windows.Controls.CheckBox checkBoxItem)
                        {
                            checkBoxItem.IsEnabled = false;
                        }
                    }
                }

                CheckBox15.IsEnabled = true;
            }
            else
            {
                _channelInfo[14] = 0;

                _lineGraphs[_itemcount * 2 - 1].Visibility = Visibility.Collapsed;
                _lineGraphs[_itemcount * 2 - 2].Visibility = Visibility.Collapsed;
                _itemcount--;

                if (_itemcount > 2)
                {
                    for (int i = 0; i < ChannelList.Children.Count; i++)
                    {
                        var item = ChannelList.Children[i];
                        if (item is System.Windows.Controls.CheckBox checkBoxItem)
                        {
                            checkBoxItem.IsEnabled = true;
                        }
                    }
                }
            }
        }

        private void CheckBox16_Click(object sender, RoutedEventArgs e)
        {
            if (CheckBox16.IsChecked == true)
            {
                _channelInfo[15] = 1;

                _itemcount++;
                _lineGraphs[_itemcount * 2 - 1].Visibility = Visibility.Visible;
                _lineGraphs[_itemcount * 2 - 2].Visibility = Visibility.Visible;

                if (_itemcount > 3)
                {
                    for (int i = 0; i < ChannelList.Children.Count; i++)
                    {
                        var item = ChannelList.Children[i];
                        if (item is System.Windows.Controls.CheckBox checkBoxItem)
                        {
                            checkBoxItem.IsEnabled = false;
                        }
                    }
                }

                CheckBox16.IsEnabled = true;
            }
            else
            {
                _channelInfo[15] = 0;

                _lineGraphs[_itemcount * 2 - 1].Visibility = Visibility.Collapsed;
                _lineGraphs[_itemcount * 2 - 2].Visibility = Visibility.Collapsed;
                _itemcount--;

                if (_itemcount > 2)
                {
                    for (int i = 0; i < ChannelList.Children.Count; i++)
                    {
                        var item = ChannelList.Children[i];
                        if (item is System.Windows.Controls.CheckBox checkBoxItem)
                        {
                            checkBoxItem.IsEnabled = true;
                        }
                    }
                }
            }
        }


        private void FreqChange_Click(object sender, RoutedEventArgs e)
        {

            var commandBuf = new byte[16];

            commandBuf[0] = 0x01;
            commandBuf[1] = 0x01;
            commandBuf[2] = 0x03;
            commandBuf[3] = (byte)FreqCombox.SelectedIndex;

            _server.Send(_connectId, commandBuf, 4);
            _samFreq = int.Parse(FreqCombox.SelectedItem.ToString().Trim());
            SampleContent.Content = FreqCombox.SelectedItem + "Hz";
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            var mDialog = new FolderBrowserDialog();
            DialogResult result = mDialog.ShowDialog();

            if (result == System.Windows.Forms.DialogResult.Cancel)
            {
                return;
            }
            _filePath.Path = mDialog.SelectedPath.Trim();
        }

        private void Duration_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {

            if (e.Key >= Key.NumPad0 && e.Key <= Key.NumPad9)
            {
                e.Handled = false;
            }
            else if (e.Key >= Key.D0 && e.Key <= Key.D9)
            {
                e.Handled = false;
            }
            else
            {
                e.Handled = true;
            }

        }

        [StructLayout(LayoutKind.Sequential)]
        public class ClientInfo
        {
            public IntPtr ConnId { get; set; }
            public string IpAddress { get; set; }
            public ushort Port { get; set; }
        }

        private void ChannelNum_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key >= Key.NumPad0 && e.Key <= Key.NumPad9)
            {
                e.Handled = false;
            }
            else if (e.Key >= Key.D0 && e.Key <= Key.D9)
            {
                e.Handled = false;
            }
            else
            {
                e.Handled = true;
            }
        }

        private void Min_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key >= Key.NumPad0 && e.Key <= Key.NumPad9)
            {
                e.Handled = false;
            }
            else if (e.Key >= Key.D0 && e.Key <= Key.D9)
            {
                e.Handled = false;
            }
            else
            {
                e.Handled = true;
            }
        }

        private void Max_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key >= Key.NumPad0 && e.Key <= Key.NumPad9)
            {
                e.Handled = false;
            }
            else if (e.Key >= Key.D0 && e.Key <= Key.D9)
            {
                e.Handled = false;
            }
            else
            {
                e.Handled = true;
            }
        }

        private void FreqText_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key >= Key.NumPad0 && e.Key <= Key.NumPad9)
            {
                e.Handled = false;
            }
            else if (e.Key >= Key.D0 && e.Key <= Key.D9)
            {
                e.Handled = false;
            }
            else
            {
                e.Handled = true;
            }
        }

        private void Threshold_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key >= Key.NumPad0 && e.Key <= Key.NumPad9)
            {
                e.Handled = false;
            }
            else if (e.Key >= Key.D0 && e.Key <= Key.D9)
            {
                e.Handled = false;
            }
            else
            {
                e.Handled = true;
            }
        }

        private void ProtNum_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key >= Key.NumPad0 && e.Key <= Key.NumPad9)
            {
                e.Handled = false;
            }
            else if (e.Key >= Key.D0 && e.Key <= Key.D9)
            {
                e.Handled = false;
            }
            else
            {
                e.Handled = true;
            }
        }

        private void IpAddress_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key >= Key.NumPad0 && e.Key <= Key.NumPad9)
            {
                e.Handled = false;
            }
            else if (e.Key >= Key.D0 && e.Key <= Key.D9)
            {
                e.Handled = false;
            }
            else if (e.Key == Key.Decimal)
            {
                e.Handled = false;
            }
            else
            {
                e.Handled = true;
            }
        }

        private void Duration_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            try
            {
                if (string.IsNullOrEmpty(Duration.Text))
                {
                    return;
                }

                int num = int.Parse(Duration.Text);

                if (num > 12)
                {
                    AddMsg($"辨识时间不可超过12s");
                    Duration.Text = Duration.Text.Remove(1);
                    Duration.SelectionStart = Duration.Text.Length;
                }

            }
            catch (Exception ex)
            {
                AddMsg($"内部错误，请联系开发商");
            }
        }

        private void ChannelNum_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            try
            {
                if (string.IsNullOrEmpty(ChannelNum.Text))
                {
                    return;
                }

                int num = int.Parse(ChannelNum.Text);

                if (num > 16)
                {
                    AddMsg($"通道最大数量为16");
                    ChannelNum.Text = ChannelNum.Text.Remove(1);
                    ChannelNum.SelectionStart = ChannelNum.Text.Length;
                }

            }
            catch (Exception ex)
            {
                AddMsg($"内部错误，请联系开发商");
            }

        }

        private void Min_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            try
            {
                if (string.IsNullOrEmpty(Min.Text))
                {
                    return;
                }

                int num = int.Parse(Min.Text);

                if (num < 100)
                {
                    AddMsg($"扫频信号起始频率不得少于100Hz");
                    Min.SelectionStart = Min.Text.Length;
                }

            }
            catch (Exception ex)
            {
                AddMsg($"内部错误，请联系开发商");
            }
        }

        private void Max_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            try
            {
                if (string.IsNullOrEmpty(Max.Text))
                {
                    return;
                }

                int num = int.Parse(Max.Text);

                if (num > _samFreq / 2)
                {
                    AddMsg($"扫频信号最大频率不得大于当前采样率的二分之一");
                    Max.Text = Max.Text.Remove(1);
                    Max.SelectionStart = Max.Text.Length;
                }

            }
            catch (Exception ex)
            {
                AddMsg($"内部错误，请联系开发商");
            }
        }
    }
}