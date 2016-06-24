﻿using System;
using System.Collections;
using System.Threading;
using Microsoft.SPOT;
using Microsoft.SPOT.Presentation.Controls;
using Microsoft.SPOT.Presentation.Media;
using Microsoft.SPOT.Presentation.Shapes;
using Microsoft.SPOT.Touch;
using Microsoft.SPOT.Net.NetworkInformation;
using tempuri.org;
using Gadgeteer.Networking;
using GT = Gadgeteer;
using GTM = Gadgeteer.Modules;
using System.Net.Sockets;
using System.Net;
using System.Text;
using Gadgeteer.Modules.GHIElectronics;
using Ws.Services.Binding;
using Ws.Services;
using System.Security.Cryptography;

using Gadgeteer.SocketInterfaces;

namespace Client
{
    public partial class Program
    {
        public const String DEFAULT_MY_IP = "192.168.137.2";
        public const String DEFAULT_DESTINATION_IP = "192.168.137.1";
        public const String DEFAULT_PORT = "1500";
        public const String MASK = "255.255.255.0";
        public const String SERVICE_ADDR = "http://192.168.137.1:8733/Service/";
        public const String KEEP_ALIVE_COMMAND = "keepAlive-";
        public const String MANAGE_IMAGE_COMMAND = "manageImage-";
        public const String FIRST_IMAGE_COMMAND = "firstImage-";
        private const string ALARM = "yes";
        private const string NOALARM = "no-";
        private const int CAMERA_INTERVAL = 3000;
        String[] connectionInfo = { DEFAULT_DESTINATION_IP, DEFAULT_PORT };
        private DigitalInput pir_sensor = null;
        private PwmOutput buzzer_sensor = null;
        private bool throw_allarm = false;
        private PwmOutput movimento_orizzontale=null,movimento_verticale=null;
        private Joystick.Position joystickPosition;
        private double current_orizzontal_pos = 0, current_vertical_pos=0;
        private string myMac;
        Bitmap bitmapA = null;
        Int32 RGlobal;
        Int32 PreviousAverage;
        long servertime = 0;
        private GT.Timer timer_joystick;
        private Boolean StopMe = false;
        private Boolean setupComplete = false;
        private Boolean NetworkUp = false;
        IService1ClientProxy proxy;
        IPEndPoint serverEndPoint = null;  // represent the connection with the server
        TimeSpan interval = new TimeSpan(0, 0, 30);
        
        /**
         * This method is run when the mainboard is powered up or reset.   
         */
        void ProgramStarted()
        {
            Debug.Print("Program Started");     // to show messages in "Output" window during debugging
            Mainboard.SetDebugLED(true);

            Thread.Sleep(300);
            InitSensors();
          
            SetupEthernet();
            ethernetJ11D.NetworkUp += OnNetworkUp;
            ethernetJ11D.NetworkDown += OnNetworkDown;

            camera.PictureCaptured += camera_PictureCaptured;
            camera.BitmapStreamed += camera_BitmapStreamed;
          

            WindowsManager.showWindowInsertPin();
        }


// ------------------------- State Machine ------------------------- //

        /**
         * This method is called when start the camera setup with joystick
         */
        private GHI.Glide.UI.ProgressBar progress;
        private GT.Timer timer_progress;
        public void setupCamera()
        {
            if (camera.CameraReady)
            {
                button.ButtonPressed += button_ButtonPressed;
                camera.StartStreaming();
                progress = WindowsManager.showWindowProgress();
                timer_progress = new GT.Timer(350);
                timer_progress.Tick += progressIncrement;
                timer_progress.Start();
            }  
        }

        /**
         * This method is called when a new streamed image is captured
         */
        private Boolean invalidate = true;
        private void camera_BitmapStreamed(GTM.GHIElectronics.Camera sender, Bitmap e)
        {
            if (invalidate)
            {
                timer_progress.Stop();
                progress.Value = 100;
                progress.Invalidate();
                invalidate = false;
                setupJoystick();
            }
            Debug.Print("Refresh streaming");
            displayT35.SimpleGraphics.DisplayImage(e, 0, 0);
        }

        /**
         * This method is triggered when the button is pressed.
         */
        private void button_ButtonPressed(GTM.GHIElectronics.Button sender, GTM.GHIElectronics.Button.ButtonState state)
        {
            Debug.Print("Button pressed!");
            button.ButtonPressed += null;
            camera.StopStreaming();
            timer_joystick.Stop();
            Thread.Sleep(300);

            if (NetworkUp)
            {
                // WindowsManager.setupWindowInsertPin();
                initServer();
            }
            else
            {
                Debug.Print("Network_Down!!!");
                WindowsManager.showWindowNetworkDown();
            }

        }

        /**
         * This method initialize the connection with the server
         * and get the port of the server socket to connect
         */
        private GT.Timer timer_keepAlive;
        private void initServer()
        {
            bindProxyService();
            getAddressAndPort();
           
            IPAddress ipAddress = IPAddress.Parse(connectionInfo[0]);
            int port = int.Parse(connectionInfo[1]);
            if (port == -1)
            {
                Debug.Print("Error: Invalid Port, impossible to establish a connection!\n");
                //TODO terminare applicazione
                return;
            }
            serverEndPoint = new IPEndPoint(ipAddress, port);

            timer_keepAlive = new GT.Timer(35000);
            timer_keepAlive.Tick += keepAlive;
            timer_keepAlive.Start();

            setupComplete = true;
            Thread.Sleep(500);
            camera.TakePicture();
        }

        /**
         * This method binding the service
         */
        private void bindProxyService()
        {
            Debug.Print("Binding proxy service...");
            proxy = new IService1ClientProxy(new WS2007HttpBinding(), new ProtocolVersion11());

            // NOTE: the endpoint needs to match the endpoint of the servicehost
            proxy.EndpointAddress = SERVICE_ADDR;

            Debug.Print("Binding proxy service COMPLETE");
        }

        /**
         * This method get the port of the server socket to connect from the service
         */
        private void getAddressAndPort()
        {
            try
            {
                var data = proxy.getServerAddressWithPort(new getServerAddressWithPort()
                {
                    myMacAddress = myMac,

                });

                connectionInfo[0] = data.getServerAddressWithPortResult.address;
                connectionInfo[1] = data.getServerAddressWithPortResult.port;
                Debug.Print("Server address: " + data.getServerAddressWithPortResult.address);
                Debug.Print("Server port: " + data.getServerAddressWithPortResult.port);
                servertime = data.getServerAddressWithPortResult.serverTime;
                Debug.Print("Server time: " + servertime);

            }
            catch (SocketException e)
            {
                WindowsManager.showWindowErrorService();
            }
           
        }

        /**
         * This method send periodic keep alive to server
         */
        private void keepAlive(GT.Timer timer)
        {
            Debug.Print("Keep Alive !");
            servertime += 35000;
            try
            {
                var data = proxy.keepAlive(new keepAlive()
                {
                    myMacAddress = myMac,
                    mycurrentTime = servertime,
                    port = int.Parse(connectionInfo[1]),
                });
            }
            catch (SocketException e)
            {

                WindowsManager.showWindowErrorService();
            }   
        }

        /**
         * This method is triggered when an image is caught.
         */
        private void camera_PictureCaptured(GTM.GHIElectronics.Camera sender, GT.Picture picture)
        {
            if (throw_allarm || picture==null)
                return;

            Int32 HeurSum = 0;
            Bitmap bitmapB = picture.MakeBitmap();

            Debug.Print("Image captured! ");
            try
            {
                if (bitmapA == null)    //per gestire la prima volta
                {
                    bitmapA = bitmapB;
                    RGlobal = heuristicSum(bitmapA);
                    PreviousAverage = RGlobal / 9;
                    sendPicture(picture.PictureData, true);
                    setupCameraTakePicture();
                    WindowsManager.showWindowInsertPin();
                    return;
                }

                Debug.Print(DateTime.Now.ToString());
                HeurSum = heuristicSum(bitmapB);
                Debug.Print(DateTime.Now.ToString());

                Debug.Print(PreviousAverage.ToString());
                Int32 average = (HeurSum / 9);
                Debug.Print(average.ToString());

                if (System.Math.Abs(PreviousAverage - average) > 45)    //SOGLIA LIMITE 40/50
                {
                    Debug.Print("Suspicious picture!");
                    sendPicture(picture.PictureData, false);
                }

                RGlobal = HeurSum;
                PreviousAverage = average;

              //  displayT35.SimpleGraphics.DisplayImage(picture, 0, 0); //TODO eliminare alla fine
            }
            catch (SocketException e)
            {
                timer_getimage.Stop();
                timer_keepAlive.Stop();
                WindowsManager.showWindowErrorServer();
            }
          
        }

        /**
         * This method get periodic image from the camera
         */
        GT.Timer timer_getimage;
        private void setupCameraTakePicture()
        {
            timer_getimage = new GT.Timer(CAMERA_INTERVAL);
            timer_getimage.Tick += takePicture;
            timer_getimage.Start();
        }

        /**
         * This method get picture
         */
        private void takePicture(GT.Timer timer)
        {
            if (camera.CameraReady)
                camera.TakePicture();
            else
            {
                Debug.Print("The camera is not ready yet");
            }
        }

        /**
         * This method send the suspicious image to the server
         */
        private void sendPicture(byte[] e, Boolean first)
        {
            byte[] cmd;
            Socket clientSocket = new Socket(
                AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            Debug.Print("Connecting to server " + serverEndPoint + ".");
            clientSocket.Connect(serverEndPoint);
            Debug.Print("Connected to server.");

            if (first)
            {
                cmd = Encoding.UTF8.GetBytes(FIRST_IMAGE_COMMAND);
            }
            else
            {
                cmd = Encoding.UTF8.GetBytes(MANAGE_IMAGE_COMMAND);
            }

            clientSocket.Send(cmd);
            clientSocket.Send(e);

            String response = reciveResponse(clientSocket);
            Debug.Print(response);

            if (response.Equals(ALARM))
            {
                ThrowAllarm();
            }

            clientSocket.Close();
        }

        /**
         * This method sum the value of green of 9 squares
         * of dimension 8x8 taken from a bitmap.
         */
        public Int32 heuristicSum(Microsoft.SPOT.Bitmap bitmapB)
        {
            Int32 RA = 0;
            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    RA += ColorUtility.GetGValue(bitmapB.GetPixel(x, y));
                }
            }

            for (int y = 116; y < 124; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    RA += ColorUtility.GetRValue(bitmapB.GetPixel(x, y));
                }
            }

            for (int y = 232; y < 240; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    RA += ColorUtility.GetRValue(bitmapB.GetPixel(x, y));
                }
            }

            for (int y = 232; y < 240; y++)
            {
                for (int x = 156; x < 164; x++)
                {
                    RA += ColorUtility.GetRValue(bitmapB.GetPixel(x, y));
                }
            }

            for (int y = 116; y < 124; y++)
            {
                for (int x = 156; x < 164; x++)
                {
                    RA += ColorUtility.GetRValue(bitmapB.GetPixel(x, y));
                }
            }

            for (int y = 0; y < 8; y++)
            {
                for (int x = 156; x < 164; x++)
                {
                    RA += ColorUtility.GetGValue(bitmapB.GetPixel(x, y));
                }
            }

            for (int y = 0; y < 8; y++)
            {
                for (int x = 312; x < 320; x++)
                {
                    RA += ColorUtility.GetGValue(bitmapB.GetPixel(x, y));
                }
            }

            for (int y = 116; y < 124; y++)
            {
                for (int x = 312; x < 320; x++)
                {
                    RA += ColorUtility.GetRValue(bitmapB.GetPixel(x, y));
                }
            }

            for (int y = 232; y < 240; y++)
            {
                for (int x = 312; x < 320; x++)
                {
                    RA += ColorUtility.GetRValue(bitmapB.GetPixel(x, y));
                }
            }

            return RA;
        }


        /**
         * This method throw allarm
         */
        private GT.Timer allarm;
        private void ThrowAllarm()
        {
            throw_allarm = true;
            timer_getimage.Stop();
            allarm = new GT.Timer(100);
            allarm.Tick += allarm_Tick;
            allarm.Start();
        }

// ------------------------- End State Machine ------------------------- //
      

// ------------------------- Network & Connections ------------------------- //
        void SetupEthernet()
        {
            ethernetJ11D.UseStaticIP(DEFAULT_MY_IP, MASK, DEFAULT_DESTINATION_IP);
            ethernetJ11D.UseThisNetworkInterface();
        }

        /**
         * This method is triggered when the network goes up.
         */ 
        private void OnNetworkUp(GTM.Module.NetworkModule sender, GTM.Module.NetworkModule.NetworkState state)
        {
            Debug.Print("Network up!");
            NetworkUp = true;
            multicolorLED.TurnGreen();
            ListNetworkInterfaces();
        }
 
        /**
         * This method is triggered when the network goes down.
         * It triggers the alarm.
         */
        private void OnNetworkDown(GTM.Module.NetworkModule sender, GTM.Module.NetworkModule.NetworkState state)
        {
            Debug.Print("Network down!");
            NetworkUp = false;
            multicolorLED.TurnRed();
          
            if (setupComplete)
            {
                timer_keepAlive.Stop();
                ThrowAllarm();
            }
                
        }

        /**
         * This method recive a buffer from the server
         */
        private string reciveResponse(Socket clientSocket)
        {
            byte[] buffer = new byte[1000];
            int ricevuti = 0;
            char vOut = 'a';
            while (vOut != '-')
            {
                ricevuti += clientSocket.Receive(buffer, ricevuti, 1, SocketFlags.None);
                vOut = Convert.ToChar(buffer[ricevuti - 1]);

            }

            byte[] buf = new byte[ricevuti];

            for (int i = 0; i < ricevuti - 1; i++)
            {
                buf[i] = buffer[i];
            }

            return new string(Encoding.UTF8.GetChars(buf));
        }


        public Boolean checkLogin(String pin)
        {
           // HashAlgorithm hashSHA256 = new HashAlgorithm(HashAlgorithmType.MD5);
            Byte[] dataToHmac = System.Text.Encoding.UTF8.GetBytes(pin);

            if (setupComplete)
            {
                var data = proxy.isValid(new isValid()
                {
                    mac = myMac,
                    pin = dataToHmac

                });

                if (data.isValidResult)
                {
                    deactivateSystem();
                }
                return false ;
            }

            bindProxyService();

            var data2 = proxy.isValid(new isValid()
            {
                mac = myMac,
                pin = dataToHmac
              
            });

            return data2.isValidResult;

        }

        private void deactivateSystem()
        {
            SpegniAllarme();
        }
// ----------------------- End Network & Connections ----------------------- //

        private void joystick_function(GT.Timer timer)
        {
            double realX = 0, realY = 0;
            Joystick.Position newJoystickPosition = joystick.GetPosition();
            double newX = joystickPosition.X;
            double newY = joystickPosition.Y;
            joystickPosition = newJoystickPosition;
       

            // did we actually move...
            if (System.Math.Abs(newX) >= 0.05)
            {
                realX = newX;
            }
            if (System.Math.Abs(newY) >= 0.05)
            {
                realY = newY;
            }
            if (realX == 0.0 && realY == 0.0)
                return;
            if (System.Math.Abs(newX) >= System.Math.Abs(newY))
            {
                if (current_orizzontal_pos + realX / 80 <= 0.1 && current_orizzontal_pos + realX / 80 >= 0.05)
                {
                    movimento_orizzontale.Set(50, current_orizzontal_pos + realX / 80);
                    current_orizzontal_pos = current_orizzontal_pos + realX / 80;
                }
            }
            else
            {
                if (current_vertical_pos + realY / 80 <= 0.1 && current_vertical_pos + realY / 80 >= 0.05)
                {
                    movimento_verticale.Set(50, current_vertical_pos + realY / 80);
                    current_vertical_pos = current_vertical_pos + realY / 80;
                }
            }
        }

        

// -------------------------------- Sensori -------------------------------- //
        private void InitSensors()
        {
            Mainboard.SetDebugLED(true);
            Gadgeteer.Socket socket = Gadgeteer.Socket.GetSocket(8, true, null, null);
      //      pir_sensor = extender.CreateDigitalInput(Gadgeteer.Socket.Pin.Four,GlitchFilterMode.Off,ResistorMode.Disabled);
            buzzer_sensor = extender.CreatePwmOutput(Gadgeteer.Socket.Pin.Nine);
            movimento_orizzontale = extender.CreatePwmOutput(Gadgeteer.Socket.Pin.Seven);
            movimento_verticale = extender.CreatePwmOutput(Gadgeteer.Socket.Pin.Eight);
            current_orizzontal_pos = 0.075;
            current_vertical_pos = 0.075;
            movimento_orizzontale.Set(50, current_orizzontal_pos);
            Thread.Sleep(2000);
            movimento_verticale.Set(50, current_vertical_pos);
             
        }

        private void setupJoystick()
        {
            joystick.Calibrate();
            timer_joystick = new GT.Timer(200);
            timer_joystick.Tick += joystick_function;
            timer_joystick.Start();

        }

        private void setupPir()
        {
            GT.Timer timer_pir = new GT.Timer(5000);
            timer_pir.Tick += PirDetection;
        }

        void allarm_Tick(GT.Timer timer)
        {
            buzzer_sensor.Set(500, 0.5);
            Thread.Sleep(1000);
            buzzer_sensor.Set(1000, 0.5);
            Thread.Sleep(1000);
        }

        private void SpegniAllarme()
        {
            allarm.Stop();
            buzzer_sensor.IsActive = false;
            buzzer_sensor.Set(0, 0);
            return;
        }

        private void progressIncrement(GT.Timer timer)
        {
            if (progress != null)
            {
                progress.Value += 5;
                progress.Invalidate();
            }
        }

        private void PirDetection(GT.Timer timer)
        {
            if (pir_sensor.Read())
            {
                Debug.Print("beccato!!!");
                //codice da eseguire quando scatta pir
            }            
        }
// ------------------------------ End Sensori ------------------------------ //


// --------------------------------- Debug --------------------------------- //
        /**
         * This method prints all the network interfaces.
         */ 
        void ListNetworkInterfaces()
        {
            var settings = ethernetJ11D.NetworkSettings;
            myMac = ByteExtensions.ToHexString(settings.PhysicalAddress, "-");

            Debug.Print("------------------------------------------------");
            Debug.Print("MAC: " + ByteExtensions.ToHexString(settings.PhysicalAddress, "-"));
            Debug.Print("IP Address:   " + settings.IPAddress);
            Debug.Print("DHCP Enabled: " + settings.IsDhcpEnabled);
            Debug.Print("Subnet Mask:  " + settings.SubnetMask);
            Debug.Print("Gateway:      " + settings.GatewayAddress);
            Debug.Print("------------------------------------------------");
        }

    }

}
