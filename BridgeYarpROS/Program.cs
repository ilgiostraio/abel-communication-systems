using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using YarpManagerCS;
using Uml.Robotics.Ros;
using std_msgs = Messages.std_msgs;


using System.Threading;

using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

using Sense.Lib.FACELibrary;
using ComUtils;

namespace bridgeYarpROS
{
    class Program
    {
        // https://docs.microsoft.com/en-us/windows/console/setconsolectrlhandler?WT.mc_id=DT-MVP-5003978
        [DllImport("Kernel32")]
        private static extern bool SetConsoleCtrlHandler(SetConsoleCtrlEventHandler handler, bool add);

        // https://docs.microsoft.com/en-us/windows/console/handlerroutine?WT.mc_id=DT-MVP-5003978
        private delegate bool SetConsoleCtrlEventHandler(CtrlType sig);

        private enum CtrlType
        {
            CTRL_C_EVENT = 0,
            CTRL_BREAK_EVENT = 1,
            CTRL_CLOSE_EVENT = 2,
            CTRL_LOGOFF_EVENT = 5,
            CTRL_SHUTDOWN_EVENT = 6
        }


        static YarpPort yarpPort;
        //static string ROS_HOSTNAME = "172.16.12.73";
        static string ROS_HOSTNAME = "172.16.12.103"; 

        static string ROS_MASTER_URI = "http://172.16.12.100:11311/"; 


        static List<string> yarpPorts       = new List<string> { "/AttentionModule/LookAt:o", "/AttentionModule/Posture:o", "/AttentionModule/Neck:o" };
        static List<string> yarpPortsType   = new List<string> { "Winner",                    "String" ,                    "String"};

        static List<string> rosTopic        = new List<string> { "/abel_control/neck/lookat", "/abel_control/arms/gesture" , "/abel_control/neck/gesture"};
        static List<string> rosTopicType    = new List<string> { "Float64MultiArray",         "String",                      "String"};



        static List<YarpPort> listYarp = new List<YarpPort>();
        static List<object> listPublishers = new List<object>();


        static bool close = false;

        static readonly ILogger logger = ApplicationLogging.CreateLogger("bridgeYarpROS");

        static async Task Main(string[] args)
        {
            SetConsoleCtrlHandler(Handler, true);

            //ApplicationLogging.ConsoleLogLevel = LogLevel.Debug;

            Environment.SetEnvironmentVariable("ROS_HOSTNAME", ROS_HOSTNAME, EnvironmentVariableTarget.User);
            Environment.SetEnvironmentVariable("ROS_MASTER_URI", ROS_MASTER_URI, EnvironmentVariableTarget.User);

            
            // Trace.Listeners.Add(new TextWriterTraceListener(Console.Out));


            ROS.Init(null, "bridgeYarpROS");

            //while (ROS.OK && !ROS.ShuttingDown)
            //{
            //    try 
            //    {
            //        if(Master.Check().Result)
            //            return;
            //    }
            //    catch 
            //    {
            //        ROS.Error()("For this to work, you need to connect to the master");
            //        Thread.Sleep(Master.RetryTimeout.Milliseconds + 1000);
            //    }

            //}

            //var result = false;
            //try
            //{
            //    result=  Task.Run(async () => await Master.Check()).Result;
            //}
            //catch { }


            if (ROS.OK)
            {
                
                yarpPort = new YarpPort();
                if (yarpPort != null && yarpPort.NetworkExists())
                {
                    for (int idx = 0; idx < yarpPorts.Count(); idx++)
                    {
                        YarpPort tmpPort = new YarpPort();

                        if (!tmpPort.PortExists(yarpPorts[idx]))
                            logger.LogWarning(yarpPorts[idx] + " non trovato");


                        //openYarpPort
                        tmpPort.openReceiver(yarpPorts[idx], "/bridgeYarpROS/" + yarpPorts[idx].Split('/')[2].Replace(":o", ":i").ToString());
                        listYarp.Add(tmpPort);

                        //create Publicher Ros
                        if (idx > rosTopic.Count())
                        {
                            listPublishers.Add(new NodeHandle().Advertise<std_msgs.String>(yarpPorts[idx].Replace(":o", ""), 10));
                            logger.LogInformation("Created Generic Topic: " + yarpPorts[idx].Replace(":o", ""));
                            logger.LogInformation("Type Message Topic: String");
                        }
                        else
                        {
                            switch (rosTopicType[idx])
                            {
                                case "Float64MultiArray":
                                    listPublishers.Add(new NodeHandle().Advertise<std_msgs.Float64MultiArray>(rosTopic[idx], 10));
                                    break;

                                case "Int64":
                                    listPublishers.Add(new NodeHandle().Advertise<std_msgs.Int64>(rosTopic[idx], 10));
                                    break;
                                case "String":
                                default:
                                    listPublishers.Add(new NodeHandle().Advertise<std_msgs.String>(rosTopic[idx], 10));
                                    break;
                            }
                            logger.LogInformation("Created Topic: " + rosTopic[idx]);
                            logger.LogInformation("Type Message Topic: " + rosTopicType[idx]);
                        }


                        ThreadPool.QueueUserWorkItem(receiveDataTimer_Elapsed, new object[] { idx });

                        logger.LogInformation("____________________________________________");


                    }
                }
                else
                    logger.LogError("yarpserver non trovato");
            }
            else
                logger.LogError("ROS non trovato");




            ROS.WaitForShutdown();

        }


     
  
        static void receiveDataTimer_Elapsed(object sender)
        {
            object[] array = sender as object[];
            int x = Convert.ToInt32(array[0]);
            string received = "";
            while (!close)
            {
                listYarp[x].receivedData(out received);
                if (received != null && received != "")
                {
                    logger.LogInformation(received);
                    switch (rosTopicType[x])
                    {
                        case "Float64MultiArray":
                            if (yarpPortsType[x] == "Winner")
                            {
                                //lookat.data = [x, y, duration - 1]
                                Winner SubjectWin = ComUtils.XmlUtils.Deserialize<Winner>(received);
                                std_msgs.Float64MultiArray d = new std_msgs.Float64MultiArray();
                                d.data = new double[] { Convert.ToDouble(SubjectWin.spinX), Convert.ToDouble(SubjectWin.spinY) };
                                ((Publisher<std_msgs.Float64MultiArray>)listPublishers[x]).Publish(d);
                            }

                            break;
                        case "Int64":
                            if (yarpPortsType[x] == "String")
                            {
                                std_msgs.Int64 d = new std_msgs.Int64();
                                d.data = Convert.ToInt64(received);
                                ((Publisher<std_msgs.Int64>)listPublishers[x]).Publish(d);
                            }

                            break;
                        case "String":
                        default:
                            ((Publisher<std_msgs.String>)listPublishers[x]).Publish(new std_msgs.String(received));
                            break;


                    }
                }
            }
        }

        //private void sendData(string msg)
        //{
        //    if (ROS.OK)
        //    {

        //        var elapsedMs = watch.ElapsedMilliseconds;
        //        watch.Restart();
        //        lblYarpPort.Invoke(new Action(() =>
        //        {
        //            lblYarpPort.Text = "Speed: " + elapsedMs.ToString() + " - " + tsk.getNumTaskRunning() + " " + tsk.getNumQueueTask();
        //        }));


        //        Console.WriteLine("publishing message");

        //        Messages.std_msgs.String pow = new Messages.std_msgs.String(msg);
        //        Talker.Publish(pow);


        //        //spinner.SpinOnce();
        //    }
        //    else if (yarpPort != null)
        //    {
        //        yarpPort.sendData(msg);
        //    }
        //}

        //ROS.Shutdown();

        

        private static bool Handler(CtrlType signal)
        {
            switch (signal)
            {
                case CtrlType.CTRL_BREAK_EVENT:
                case CtrlType.CTRL_C_EVENT:
                case CtrlType.CTRL_LOGOFF_EVENT:
                case CtrlType.CTRL_SHUTDOWN_EVENT:
                case CtrlType.CTRL_CLOSE_EVENT:
                    Console.WriteLine("Closing");
                    close = true;

                    foreach (YarpPort y in listYarp)
                        y.Close();

                   

                    ROS.Shutdown();

                    Environment.Exit(0);
                    return false;

                default:
                    return false;
            }
        }
    }
}
