﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Win32;
using System.Diagnostics;
using System.Net.Sockets;

namespace HdhrStreamServer
{
    class StreamServerController
    {
        private String vlcExe;
        private Process vlcProcess = null;

        private int mControlPort = 5454;
        public int ControlPort
        {
            get { return mControlPort; }
            set { mControlPort = value; }
        }

        private int mVideoInputPort;
        public int VideoInputPort
        {
            get { return mVideoInputPort; }
            set { mVideoInputPort = value; }
        }

        private int mVideoOutputPort;
        public int VideoOutputPort
        {
            get { return mVideoOutputPort; }
            set { mVideoOutputPort = value; }
        }

        private int mBitRate;
        public int BitRate
        {
            get { return mBitRate; }
            set { mBitRate = value; }
        }

        private int mResH;
        public int ResH
        {
            get { return mResH; }
            set { mResH = value; }
        }

        private int mResV;
        public int ResV
        {
            get { return mResV; }
            set { mResV = value; }
        }

        private AsyncSocketServer mServer;
        private Socket mClient;

        //EVENTS
        public delegate void ServerStartedHandler();
        public event ServerStartedHandler ServerStarted;

        public delegate void ServerStoppedHandler();
        public event ServerStoppedHandler ServerStopped;

        public delegate void ServerStreamingHandler();
        public event ServerStreamingHandler ServerStreaming;

        public delegate void ServerStreamingStoppedHandler();
        public event ServerStreamingStoppedHandler ServerStreamingStopped;

        public StreamServerController( int controlPort, int inputPort, int outputPort )
        {
            VideoInputPort = inputPort;
            VideoOutputPort = outputPort;
            ControlPort = controlPort;

            ResH = 480;
            ResV = 320;
            BitRate = 128;

            //create server
            mServer = new AsyncSocketServer(ControlPort);

            //subscribe to events
            mServer.AcceptComplete += new AsyncSocketServer.AcceptHandler(server_AcceptComplete);
            mServer.DataReceived += new AsyncSocketServer.DataReceivedHandler(server_DataReceived);
            mServer.SendComplete += new AsyncSocketServer.SendCompleteHandler(server_SendComplete);
        }

        public void Start()
        {

            if (vlcExe == null)
            {
                throw new ViewerNotFoundException("VLC is not setup");
            }

            //start listening
            mServer.Accept();

            ServerStarted();
        }

        private void server_SendComplete()
        {
            Console.WriteLine("Send is complete");
        }

        private void server_DataReceived(string data)
        {
            Console.WriteLine("Recieved {0} from client", data);

            String[] commandArray = data.Split('>');
            String[] idAndCommand = commandArray[0].Split(':');

            String id = idAndCommand[0];
            String command = idAndCommand[1];
            command.TrimEnd(null);


            String args = "";
            if (commandArray.Length > 1)
            {
                args = commandArray[1];
            }

            try
            {
                processCommand(id, command, args);

                //System.Threading.Thread.Sleep(1000);
                mServer.Send(mClient, String.Format("ACK:INPUT {0},OUTPUT {1}\n",VideoInputPort,VideoOutputPort));
            }
            catch (BadCommandException e)
            {
                Console.WriteLine("Command failed: {0}", e.Message);
                mServer.Send(mClient, "FAIL\n");
            }
        }

        private void processCommand(string id, string command, string args)
        {
            Console.WriteLine("ID: {0}, command {1}, args {2}", id, command, args);

            if(command.IndexOf("SETUP") > -1)
            {
                processSetup(id, args);
            }    
            else if(command.IndexOf("BYE") > -1)
            {
                mServer.Close(mClient);
            }
            else if (command.IndexOf("START") > -1)
            {
                startVLC();
            }
            else if (command.IndexOf("STOP") > -1)
            {
                stopVLC();
            }
            else
            {
                Console.WriteLine("Unknown command {0}", command);
                throw new BadCommandException(String.Format("Unknown command {0}", command));
            }
        }

        private void processSetup(string id, string args)
        {
            String[] argList = args.Split(',');

            foreach (String arg in argList)
            {
                String[] keyValPair = arg.Split(' ');
                String key = keyValPair[0];
                String val = keyValPair[1];

                switch (key)
                {
                    case "BITRATE":
                        BitRate = Convert.ToInt32(val);
                        break;
                    case "RESH":
                        ResH = Convert.ToInt32(val);
                        break;
                    case "RESV":
                        ResV = Convert.ToInt32(val);
                        break;
                    default:
                        Console.WriteLine("Unknown Key {0}", key);
                        throw new BadCommandException(String.Format("Unknown Key {0}", key));
                }
                
            }

        }

        private void server_AcceptComplete(Socket client)
        {
            Console.WriteLine("Client is connected");
            mClient = client;

            //TODO randomize ports here
        }

        private void startVLC()
        {
            ProcessStartInfo start = new ProcessStartInfo();

            start.FileName = vlcExe;
            start.Arguments = buildVlcArguments();

            
            stopVLC();

            try
            {
                vlcProcess = Process.Start(start);
                ServerStreaming();
            }
            catch (System.ComponentModel.Win32Exception aException)
            {
                System.Windows.MessageBox.Show("Failed to start VLC. Please check the VLC Path\nError: " + aException.Message, "Error", System.Windows.MessageBoxButton.OK);
            }            
        }

        private void stopVLC()
        {
            if (vlcProcess != null)
            {
                vlcProcess.Kill();
                vlcProcess.WaitForExit();

                vlcProcess = null;
            }

            ServerStreamingStopped();
        }



        private string buildVlcArguments()
        {
            StringBuilder cmdList = new StringBuilder();
            //cmdList.Append("--file-logging ");
            //cmdList.Append("--log-verbose=-1 ");
            //cmdList.Append("--logmode=html ");
            //cmdList.Append("--logfile=" + System.IO.Directory.GetCurrentDirectory() + "\\log.html ");
            cmdList.Append("--no-crashdump ");
            cmdList.Append("--sout-ffmpeg-strict=-2 ");
            cmdList.Append("-V dummy ");
            cmdList.Append("-I dummy ");
            cmdList.Append("-vvv ");
            cmdList.Append("udp://@:" + VideoInputPort + " ");
            cmdList.Append("--audio-desync=-50 ");
            //cmdList.Append("--network-caching=4096 ");
            //cmdList.Append("--udp-caching=4096 ");
            cmdList.Append("--no-sout-rtp-sap ");
            cmdList.Append("--sout-rtp-caching=3000 "); //TODO supposedly should do something with this for different bitrates but not sure what yet
            cmdList.Append("--sout-rtp-sdp=rtsp://:" + VideoOutputPort + "/stream.sdp ");
            cmdList.Append("--sout-rtp-mp4a-latm ");
            cmdList.Append("--sout-transcode-threads=" + Environment.ProcessorCount + " " );
            cmdList.Append("--sout-transcode-high-priority ");
            cmdList.Append("--sout-keep ");
            cmdList.Append("--sout-transcode-venc=x264 ");
            cmdList.Append("--sout-x264-profile=basline ");
            cmdList.Append("--sout-x264-level=3 ");
            cmdList.Append("--sout-x264-keyint=50 ");
            cmdList.Append("--sout-x264-bframes=0 ");
            cmdList.Append("--no-sout-x264-cabac ");
            cmdList.Append("--sout-x264-ref=1 ");
            cmdList.Append("--no-sout-x264-interlaced ");

            cmdList.Append("--sout-x264-vbv-maxrate=" + BitRate + " ");
            cmdList.Append("--sout-x264-vbv-bufsize=" + Math.Round((decimal)BitRate / 2) + " ");

            cmdList.Append("--sout-x264-aq-mode=0 ");
            cmdList.Append("--no-sout-x264-mbtree ");
            cmdList.Append("--sout-x264-partitions=none ");
            cmdList.Append("--no-sout-x264-weightb ");
            cmdList.Append("--sout-x264-weightp=0 ");
            cmdList.Append("--sout-x264-me=dia ");
            cmdList.Append("--sout-x264-subme=0 ");
            cmdList.Append("--no-sout-x264-mixed-refs ");
            cmdList.Append("--no-sout-x264-8x8dct ");
            cmdList.Append("--sout-x264-trellis=0 ");
            cmdList.Append("--sout-transcode-vcodec=h264 ");

            cmdList.Append("--sout-transcode-vb=" + BitRate + " ");
            cmdList.Append("--sout-transcode-vfilter=canvas ");
            cmdList.Append("--canvas-width=" + ResH + " ");
            cmdList.Append("--canvas-height=" + ResV + " ");
            cmdList.Append("--canvas-aspect=" + ResH + ":" + ResV + " ");
            cmdList.Append("--canvas-padd ");
            //cmdList.Append("--canvas-pAppend ");
            cmdList.Append("--sout-transcode-soverlay ");
            
            cmdList.Append("--sout-transcode-aenc=ffmpeg ");
            cmdList.Append("--sout-ffmpeg-aac-profile=low ");
            cmdList.Append("--sout-transcode-acodec=mp4a ");
            cmdList.Append("--sout-transcode-samplerate=44100 ");
            cmdList.Append("--sout-transcode-ab=128 ");
            cmdList.Append("--sout-transcode-channels=2 ");
            cmdList.Append("--sout-transcode-audio-sync ");
            cmdList.Append("--sout ");
            cmdList.Append("#transcode{}:rtp{}");

            System.Console.WriteLine(cmdList.ToString());

            return cmdList.ToString();
        }               

        public void Stop()
        {
            stopVLC();

            if (mServer != null)
            {
                mServer.Stop();
            }

            ServerStopped();
        }

        public void setViewer( string aPath )
        {
            vlcExe = aPath;
        }
    }

    
}
