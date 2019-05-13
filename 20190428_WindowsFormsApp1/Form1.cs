using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.IO.Ports;
using System.Threading;
using System.IO;
using System.Text.RegularExpressions;
using System.Timers;
using System.Diagnostics;
using System.Globalization;

namespace _20190428_WindowsFormsApp1
{
    public partial class Form1 : Form
    {

        System.Timers.Timer timer = new System.Timers.Timer(); // 定时器
        private SerialPort comm = new SerialPort();
        private StringBuilder builder = new StringBuilder();//避免在事件处理方法中反复的创建，定义到外面。   
        private long received_count = 0;//接收计数   
        private bool Listening = false;//是否没有执行完invoke相关操作   
        private bool Closing = false;//是否正在关闭串口，执行Application.DoEvents，并阻止再次invoke   
        private List<byte> buffer = new List<byte>(4096);//默认分配1页内存，并始终限制不允许超过   
        private byte[] binary_data_1 = new byte[9];//AA 44 05 01 02 03 04 05 EA   

        public Form1()
        {
            InitializeComponent();

        }

        private void Form1_Load(object sender, EventArgs e)
        {
            //初始化下拉串口名称列表框   
            string[] ports = SerialPort.GetPortNames();
            Array.Sort(ports);
            comboBox_Port.Items.AddRange(ports);
            comboBox_Port.SelectedIndex = comboBox_Port.Items.Count > 0 ? 0 : -1;
            comm.BaudRate = Convert.ToInt32(9600);
            comm.StopBits = StopBits.One;
            //初始化SerialPort对象   
            comm.NewLine = "\r\n";
            comm.RtsEnable = true;//根据实际情况吧。   
            //添加事件注册   
            comm.DataReceived += comm_DataReceived;
        }
        void comm_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            if (Closing) return;//如果正在关闭，忽略操作，直接返回，尽快的完成串口监听线程的一次循环   
            try
            {
                Listening = true;//设置标记，说明我已经开始处理数据，一会儿要使用系统UI的。  
                int n = comm.BytesToRead;//先记录下来，避免某种原因，人为的原因，操作几次之间时间长，缓存不一致   
                byte[] buf = new byte[n];//声明一个临时数组存储当前来的串口数据   
                received_count += n;//增加接收计数   
                comm.Read(buf, 0, n);//读取缓冲数据   
                /////////////////////////////////////////////////////////////////////////////////////////////////////////////   
                //<协议解析>   
                bool data_1_catched = false;//缓存记录数据是否捕获到   
                //1.缓存数据   
                buffer.AddRange(buf);
                //2.完整性判断   
                while (buffer.Count >= 4)//至少要包含头（2字节）+长度（1字节）+校验（1字节）   
                {
                    //请不要担心使用>=，因为>=已经和>,<,=一样，是独立操作符，并不是解析成>和=2个符号   
                    //2.1 查找数据头   
                    if (buffer[0] == 0x42 && buffer[1] == 0x4D)
                    {
                        //2.2 探测缓存数据是否有一条数据的字节，如果不够，就不用费劲的做其他验证了   
                        //前面已经限定了剩余长度>=4，那我们这里一定能访问到buffer[2]这个长度  
                        int len = buffer[2]*256+buffer[3];//数据长度   
                        //数据完整判断第一步，长度是否足够   
                        //len是数据段长度,4个字节是while行注释的3部分长度   
                        if (buffer.Count < len + 4) break;//数据不够的时候什么都不做   
                        //这里确保数据长度足够，数据头标志找到，我们开始计算校验   
                        //2.3 校验数据，确认数据正确   
                        //异或校验，逐个字节异或得到校验码   
                        byte checksum = 0;
                        for (int i = 0; i < len + 4; i++)//len+3表示校验之前的位置   
                        {
                            checksum ^= buffer[i];
                        }
                        if (checksum != buffer[len + 3]) //如果数据校验失败，丢弃这一包数据   
                        {
                            buffer.RemoveRange(0, len + 3);//从缓存中删除错误数据   
                            continue;//继续下一次循环   
                        }
                        //至此，已经被找到了一条完整数据。我们将数据直接分析，或是缓存起来一起分析   
                        //我们这里采用的办法是缓存一次，好处就是如果你某种原因，数据堆积在缓存buffer中   
                        //已经很多了，那你需要循环的找到最后一组，只分析最新数据，过往数据你已经处理不及时   
                        //了，就不要浪费更多时间了，这也是考虑到系统负载能够降低。   
                        //buffer.CopyTo(0, binary_data_1, 0, len + 4);//复制一条完整数据到具体的数据缓存   
                        //data_1_catched = true;
                        //buffer.RemoveRange(0, len + 4);//正确分析一条数据，从缓存中移除数据。   
                    }
                    else
                    {
                        //这里是很重要的，如果数据开始不是头，则删除数据   
                        buffer.RemoveAt(0);
                    }
                }
                //分析数据   
               // if (data_1_catched)
              //  {
                    //我们的数据都是定好格式的，所以当我们找到分析出的数据1，就知道固定位置一定是这些数据，我们只要显示就可以了   
                    //string data = binary_data_1[3].ToString("X2") + " " + binary_data_1[4].ToString("X2") + " " +
                    //    binary_data_1[5].ToString("X2") + " " + binary_data_1[6].ToString("X2") + " " +
                    //    binary_data_1[7].ToString("X2");
                    // string data = binary_data_1[0].ToString("x2");

                    //更新界面   
                  //  this.Invoke((EventHandler)(delegate { textBox_Get.Text = data; }));    //richTextBox_Data    textBox_Get
             //   }
                //如果需要别的协议，只要扩展这个data_n_catched就可以了。往往我们协议多的情况下，还会包含数据编号，给来的数据进行   
                //编号，协议优化后就是： 头+编号+长度+数据+校验   
                //</协议解析>   
                /////////////////////////////////////////////////////////////////////////////////////////////////////////////   
                builder.Clear();//清除字符串构造器的内容   

                //因为要访问ui资源，所以需要使用invoke方式同步ui。   
                this.Invoke((EventHandler)(delegate
                {
                    //foreach (byte b in buf)
                    //{
                    //    if (buffer.Count > 29)
                    //    {

                            //依次的拼接出16进制字符串   
                            //foreach (byte b in buf)
                            //{
                            //    builder.Append(b.ToString("X2") + " ");
                            //}


                            //追加的形式添加到文本框末端，并滚动到最后。   
                            // this.richTextBox_Data.AppendText(builder.ToString());
                            //richTextBox_Data.Text = "  \r\n   " + System.Int32.Parse( (buf[2]*256+ buf[3]).ToString("X2"), System.Globalization.NumberStyles.HexNumber);

                            textBox1.Text = System.Int32.Parse((buf[4] * 256 + buf[5]).ToString("X2"), System.Globalization.NumberStyles.HexNumber).ToString();
                            textBox2.Text = System.Int32.Parse((buf[6] * 256 + buf[7]).ToString("X2"), System.Globalization.NumberStyles.HexNumber).ToString();
                            textBox3.Text = System.Int32.Parse((buf[8] * 256 + buf[9]).ToString("X2"), System.Globalization.NumberStyles.HexNumber).ToString();
                            textBox4.Text = System.Int32.Parse((buf[10] * 256 + buf[11]).ToString("X2"), System.Globalization.NumberStyles.HexNumber).ToString();
                            textBox5.Text = System.Int32.Parse((buf[12] * 256 + buf[13]).ToString("X2"), System.Globalization.NumberStyles.HexNumber).ToString();
                            textBox6.Text = System.Int32.Parse((buf[14] * 256 + buf[15]).ToString("X2"), System.Globalization.NumberStyles.HexNumber).ToString();
                            textBox7.Text = System.Int32.Parse((buf[16] * 256 + buf[17]).ToString("X2"), System.Globalization.NumberStyles.HexNumber).ToString();
                            textBox8.Text = System.Int32.Parse((buf[18] * 256 + buf[19]).ToString("X2"), System.Globalization.NumberStyles.HexNumber).ToString();
                            textBox9.Text = System.Int32.Parse((buf[20] * 256 + buf[21]).ToString("X2"), System.Globalization.NumberStyles.HexNumber).ToString();
                            textBox10.Text = System.Int32.Parse((buf[22] * 256 + buf[23]).ToString("X2"), System.Globalization.NumberStyles.HexNumber).ToString();
                            textBox11.Text = System.Int32.Parse((buf[24] * 256 + buf[25]).ToString("X2"), System.Globalization.NumberStyles.HexNumber).ToString();
                            textBox12.Text = System.Int32.Parse((buf[26] * 256 + buf[27]).ToString("X2"), System.Globalization.NumberStyles.HexNumber).ToString();
                            textBox13.Text = System.Int32.Parse(buf[28].ToString("X2"), System.Globalization.NumberStyles.HexNumber).ToString();
                            textBox14.Text = System.Int32.Parse(buf[29].ToString("X2"), System.Globalization.NumberStyles.HexNumber).ToString();
                    //    }
                    //}
                    //修改接收计数   
                    label_GetCount.Text = "Count:" + (received_count/32).ToString() +"  Byte:" + received_count.ToString();

                    //存储log
                    StreamWriter file = new StreamWriter("log.txt", true);
                    file.WriteLine(DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss-")
                        + textBox1.Text + "  " + textBox2.Text + "  " + textBox3.Text + "  " + textBox4.Text + "  " + textBox5.Text + "  "
                         + textBox6.Text + "  " + textBox7.Text + "  " + textBox8.Text + "  " + textBox9.Text + "  " + textBox10.Text + "  " 
                         + textBox11.Text + "  "  + textBox12.Text + "  " + textBox13.Text + "  " + textBox14.Text + "  ");
                    file.Close();

                }));
            }
            finally
            {
                Listening = false;//我用完了，ui可以关闭串口了。   
            }
            Thread.Sleep(1000);

        }

        private void SetPortProperty()//设置串口的属性
        {
            comm = new SerialPort
            {
                //初始化SerialPort对象   
                PortName = comboBox_Port.Text.Trim(), //设置串口名
           //    comboBox_Port.SelectedIndex = comboBox_Port.Items.Count > 0 ? 0 : -1;
                //comm.BaudRate = Convert.ToInt32(9600);
            BaudRate = Convert.ToInt32(9600) };
            comm.StopBits = StopBits.One;
        }
            private void Btn_Open_Click(object sender, EventArgs e)
            {
                 //根据当前串口对象，来判断操作   
                 if (comm.IsOpen)
                 {
                //Closing = true;
                while (Listening) Application.DoEvents();
                {
                    //打开时点击，则关闭串口   
                    comm.Close();   
                }
            }
                else
                {
                //关闭时点击，则设置好端口，波特率后打开   
                comm.PortName = comboBox_Port.Text;
                comm.BaudRate = Convert.ToInt32(9600);
                //comm.StopBits = StopBits.One;
                     try
                     {
                         Closing = false;
                          comm.Open();

                      }
                catch (Exception ex)
                     {
                    //捕获到异常信息，创建一个新的comm对象，之前的不能用了。   
                    comm = new SerialPort();
                    //现实异常信息给客户。   
                    MessageBox.Show(ex.Message);
                    return;
                     }
                 }
                  //设置按钮的状态
                     Btn_Open.Text = comm.IsOpen ? "Close" : "Open";
            //    if (comm.IsOpen == false)//串口未打开，按钮打开
            //    {
            //        //if (!CheckPortSetting())//检查串口参数是否非法
            //        //{
            //        //    MessageBox.Show("串口未设置！", "错误提示");
            //        //    return;
            //        //}
            //        SetPortProperty();//设置串口参数
            //        try
            //        {
            //            comm.Open();//打开串口
            //            //this.SerialSendQueue.Clear(); //发送命令队列清空
            //            //this.SerialSendWaiter.Set();  //启动串口发送线程
            //            //this.SerialRevWaiter.Set();   //启动串口接收线程
            //        }
            //        catch (Exception ex)//串口打开异常处理
            //        {
            //            //捕获到异常信息，创建一个新的sp对象，之前的不能用了
            //            comm = new SerialPort();
            //            //MessageBox.Show("串口无效或已被占用！", "错误提示");
            //            MessageBox.Show(ex.Message, "串口无效或已被占用！", MessageBoxButtons.OK, MessageBoxIcon.Error);
            //        }
            //    }
            //    else//关闭串口
            //    {
            //        try
            //        {
            //            comm.Close();//关闭串口
            //            //pictureBox1.BackColor = System.Drawing.Color.Red;
            //            //this.SerialSendQueue.Clear(); //发送命令队列清空
            //            //this.SerialRevList.Clear();   //接收数据清空
            //            //this.SerialSendWaiter.Reset();  //停止串口发送线程
            //       //     this.SerialRevWaiter.Reset();   //停止串口接收线程
            //        }
            //        catch (Exception ex)//关闭串口异常
            //        {
            //            MessageBox.Show(ex.Message, "串口关闭失败！", MessageBoxButtons.OK, MessageBoxIcon.Error);
            //        }
            //    }
            //    //设置按钮、图标的状态  
            //    Btn_Open.Text = comm.IsOpen ? "Close" : "Open";
            //    // pictureBox1.BackgroundImage = sp.IsOpen ? Properties.Resources.蓝点 : Properties.Resources.红点;
                comboBox_Port.Enabled = !comm.IsOpen; //串口打开则串口号锁定
            ////    btnSend.Enabled = sp.IsOpen; //串口未打开禁止发送
            //  //  cbTimeSend.Enabled = sp.IsOpen;//串口未打开禁止定时发送
              }



        //Save as
        private void Btn_Save_as_Click(object sender, EventArgs e)
        {
            // "保存为"对话框
            SaveFileDialog dialog = new SaveFileDialog();
            dialog.Filter = "文本文件|*.txt";
            // 显示对话框
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                // 文件名
                string fileName = dialog.FileName;
                // 创建文件，准备写入
                FileStream fs = File.Open(fileName,
                        FileMode.Create,
                        FileAccess.Write);
                StreamWriter wr = new StreamWriter(fs);

                // 逐行将textBox1的内容写入到文件中
                //foreach (string line in textBox1.Lines)
                //{
                //    wr.WriteLine(line);
                //}
                wr.WriteLine(textBox1.Text + "  " + textBox2.Text + "  " + textBox3.Text + "  " + textBox4.Text + "  " + textBox5.Text + "  "
                     + textBox6.Text + "  " + textBox7.Text + "  " + textBox8.Text + "  " + textBox9.Text + "  " + textBox10.Text + "  "
                     + textBox11.Text + "  " + textBox12.Text + "  " + textBox13.Text + "  " + textBox14.Text + "  ");
                // 关闭文件
                wr.Flush();
                wr.Close();
                fs.Close();
            }
        }

        private void Btn_Save_Click(object sender, EventArgs e)
        {
           StreamWriter file = new StreamWriter(System.AppDomain.CurrentDomain.SetupInformation.ApplicationBase + DateTime.Now.ToString("yyyy-MM-dd-HH-mm")+".txt", true);
            file.WriteLine(DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss-" +"  ")
                + textBox1.Text + "  " + textBox2.Text + "  " + textBox3.Text + "  " + textBox4.Text + "  " + textBox5.Text + "  "
                 + textBox6.Text + "  " + textBox7.Text + "  " + textBox8.Text + "  " + textBox9.Text + "  " + textBox10.Text + "  "
                 + textBox11.Text + "  " + textBox12.Text + "  " + textBox13.Text + "  " + textBox14.Text + "  ");
            file.Close();
        }
        //定时器定时事件
       // private void timerSerial_Tick(object sender, EventArgs e)
     //   {
      //      try
         //   {
              //  timer.Interval = int.Parse(Tb_Savetime.Text);//根据定时文本设置定时时间
                //Btn_Save.PerformClick();//生成按钮的 Click 事件
      //      }
        //    catch (Exception)
        //    {
        //        timer.Enabled = false;
        //        MessageBox.Show("错误的定时输入！", "错误提示");
        //    }
        //}
        //定时器开关
        private void Cb_Save_CheckedChanged(object sender, EventArgs e)
        {
            if (Cb_Save.Checked == false)
            {
                timer.Enabled = false;
            }
            else {
                try
                {
                 //   timer.Enabled = Cb_Save.Checked;//选中则打开定时器
                    timer.Interval = int.Parse(Tb_Savetime.Text);//根据定时文本设置定时时间
                    timer.Elapsed += new System.Timers.ElapsedEventHandler(Btn_Save_Click); // 到时间后执行
                    timer.AutoReset = true; // 是否一直执行
                    timer.Enabled = true; // 是否执行
                    timer.Start(); // 开始
                }
                catch { timer.Enabled = false; }

            }

        }
        //输入时间格式
        private void Tb_Savetime_KeyPress(object sender, KeyPressEventArgs e)
        {
            //通过正则匹配输入，仅支持数字和退格
            string patten = "[0-9]|\b"; //“\b”：退格键
            Regex r = new Regex(patten);
            Match m = r.Match(e.KeyChar.ToString());

            if (m.Success)//
            {
                e.Handled = false;   //没操作“过”，系统会处理事件    
            }
            else
            {
                e.Handled = true;//cancel the KeyPress event
            }
        }


    }
    
}
