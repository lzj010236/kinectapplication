/*
 *This project is modified based on two examples that provided by Microsoft:
 *SpeechBasic and SkeletonBasic
 */

namespace Microsoft.Samples.Kinect.SkeletonBasics
{
    using System;
    using System.Windows.Threading;
    using System.Threading;
    using System.Timers;
    using System.IO;
    using System.Windows;
    using System.Windows.Media;
    using System.Windows.Media.Imaging;
    using Microsoft.Kinect;
    using Microsoft.Speech.Recognition;
    using Microsoft.Speech.AudioFormat;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text;

    //main class for window
    public partial class MainWindow : Window
    {
        private int id = 0;//current skeleton id
        private System.Timers.Timer timer = new System.Timers.Timer();
        private List<Falling_Ball> falling_ball = new List<Falling_Ball>();//balls
        private int new_ball_counter = 0;//intervels to render new ball counter
        private Random rnd;//ramdom number generator
        private double jointSize = 10;//size of joints except head
        private double headSize = 50;//size of head
        private int score1, score2;
        public SpeechRecognitionEngine speechEngine;
        public String command = "hello";//speech command
        public int game_state = 0;//status of game. 0 timeup. 1 started. -1 paused.
        public int speed_level = 0;
        public int game_total_time = 30*20;
        public int game_timer = 0;
        public bool player1_win = false;
        public bool player2_win = false;
        /// <summary>
        /// Width of output drawing
        /// </summary>
        private const float RenderWidth = 640.0f;

        /// <summary>
        /// Height of our output drawing
        /// </summary>
        private const float RenderHeight = 480.0f;

        /// <summary>
        /// Thickness of drawn joint lines
        /// </summary>
        private const double JointThickness = 3;

        /// <summary>
        /// Thickness of body center ellipse
        /// </summary>
        private const double BodyCenterThickness = 10;

        /// <summary>
        /// Thickness of clip edge rectangles
        /// </summary>
        private const double ClipBoundsThickness = 10;

        /// <summary>
        /// Brush used to draw skeleton center point
        /// </summary>
        private readonly Brush centerPointBrush = Brushes.Blue;

        /// <summary>
        /// Brush used for drawing joints that are currently tracked
        /// </summary>
        private readonly Brush trackedJointBrush = new SolidColorBrush(Color.FromArgb(255, 68, 192, 68));

        /// <summary>
        /// Brush used for drawing joints that are currently inferred
        /// </summary>        
        private readonly Brush inferredJointBrush = Brushes.Yellow;

        /// <summary>
        /// Pen used for drawing bones that are currently tracked
        /// </summary>
        private readonly Pen trackedBonePen = new Pen(Brushes.Green, 6);

        /// <summary>
        /// Pen used for drawing bones that are currently inferred
        /// </summary>        
        private readonly Pen inferredBonePen = new Pen(Brushes.Gray, 1);

        /// <summary>
        /// Active Kinect sensor
        /// </summary>
        private KinectSensor sensor;

        /// <summary>
        /// Drawing group for skeleton rendering output
        /// </summary>
        private DrawingGroup drawingGroup, drawingGroup2;

        /// <summary>
        /// Drawing image that we will display
        /// </summary>
        private DrawingImage imageSource;
        private DrawingImage imageSource2;

        /// <summary>
        /// Initializes a new instance of the MainWindow class.
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();
        }



        /// <summary>
        /// Execute startup tasks
        /// </summary>
        /// <param name="sender">object sending the event</param>
        /// <param name="e">event arguments</param>
        private void WindowLoaded(object sender, RoutedEventArgs e)
        {
            //generate ramdom number
            int seed = unchecked(DateTime.Now.Ticks.GetHashCode());
            rnd = new Random(seed);

            // Create the drawing group we'll use for drawing
            this.drawingGroup = new DrawingGroup();
            this.drawingGroup2 = new DrawingGroup();
            // Create an image source that we can use in our image control

            this.imageSource = new DrawingImage(this.drawingGroup);
            this.imageSource2 = new DrawingImage(this.drawingGroup2);

            // Display the drawing using our image control
            Image.Source = this.imageSource;
            Image2.Source = this.imageSource2;

            //initialize timer for falling balls
            timer.Interval = 50;
            timer.Elapsed += timer_Elapsed;
            timer.Enabled = true;
            timer.Start();

            // Look through all sensors and start the first connected one.
            // This requires that a Kinect is connected at the time of app startup.
            // To make your app robust against plug/unplug, 
            // it is recommended to use KinectSensorChooser provided in Microsoft.Kinect.Toolkit (See components in Toolkit Browser).
            foreach (var potentialSensor in KinectSensor.KinectSensors)
            {
                if (potentialSensor.Status == KinectStatus.Connected)
                {
                    this.sensor = potentialSensor;
                    break;
                }
            }

            if (null != this.sensor)
            {
                // Turn on the skeleton stream to receive skeleton frames
                this.sensor.SkeletonStream.Enable();

                // Add an event handler to be called whenever there is new color frame data
                this.sensor.SkeletonFrameReady += this.SensorSkeletonFrameReady;

                // Start the sensor!
                try
                {
                    this.sensor.Start();
                }
                catch (IOException)
                {
                    this.sensor = null;
                }
            }

            if (null == this.sensor)
            {
                this.statusBarText.Text = Properties.Resources.NoKinectReady;
            }

            //initialize speech reconizer
            RecognizerInfo ri = GetKinectRecognizer();
            if (null != ri)
            {
                this.speechEngine = new SpeechRecognitionEngine(ri.Id);

                // Create a grammar from grammar definition XML file.
                using (var memoryStream = new MemoryStream(Encoding.ASCII.GetBytes(Properties.Resources.SpeechGrammar)))
                {
                    var g = new Grammar(memoryStream);
                    speechEngine.LoadGrammar(g);
                }

                speechEngine.SpeechRecognized += speechEngine_SpeechRecognized;
                speechEngine.SpeechRecognitionRejected += speechEngine_SpeechRecognitionRejected;

                // For long recognition sessions (a few hours or more), it may be beneficial to turn off adaptation of the acoustic model. 
                // This will prevent recognition accuracy from degrading over time.
                ////speechEngine.UpdateRecognizerSetting("AdaptationOn", 0);

                speechEngine.SetInputToAudioStream(
                    sensor.AudioSource.Start(), new SpeechAudioFormatInfo(EncodingFormat.Pcm, 16000, 16, 1, 32000, 2, null));
                speechEngine.RecognizeAsync(RecognizeMode.Multiple);
            }
        }

        void speechEngine_SpeechRecognitionRejected(object sender, SpeechRecognitionRejectedEventArgs e)
        {
            command = "Sorry I didn't hear you";
        }

        void speechEngine_SpeechRecognized(object sender, SpeechRecognizedEventArgs e)
        {
            // Speech utterance confidence below which we treat speech as if it hadn't been heard
            const double ConfidenceThreshold = 0.3;


            if (e.Result.Confidence >= ConfidenceThreshold)
            {
                command = e.Result.Semantics.Value.ToString();
            }
        }

        /// Gets the metadata for the speech recognizer (acoustic model) most suitable to
        /// process audio from Kinect device.
        /// RecognizerInfo if found, <code>null</code> otherwise.
        private static RecognizerInfo GetKinectRecognizer()
        {
            foreach (RecognizerInfo recognizer in SpeechRecognitionEngine.InstalledRecognizers())
            {
                string value;
                recognizer.AdditionalInfo.TryGetValue("Kinect", out value);
                if ("True".Equals(value, StringComparison.OrdinalIgnoreCase) && "en-US".Equals(recognizer.Culture.Name, StringComparison.OrdinalIgnoreCase))
                {
                    return recognizer;
                }
            }

            return null;
        }

        public class Falling_Ball
        {
            private double radius;
            private int color;
            private int position_x;
            private double position_y;
            private int speed;

            public Falling_Ball(Random rnd)
            {

                radius = rnd.Next(10, 20);
                color = rnd.Next(2);
                position_x = rnd.Next(10);
                position_y = 0;
                speed = rnd.Next(2, 5);
            }
            public double GetRadius() { return radius; }

            public Brush GetColor()
            {
                if (color == 0)
                    return Brushes.Blue;
                else
                    return Brushes.Red;
            }

            public int GetColorInt() { return color; }

            public double GetPosition_X()
            {
                return (position_x * 2 + 1) * RenderWidth / 20;
            }

            public void Move(double sp_mult)
            {
                position_y += (speed * sp_mult);
            }

            public double GetPosition_Y() { return position_y; }


        }

        //timer event driver
        void timer_Elapsed(object sender, ElapsedEventArgs e)
        {
            lock (falling_ball)
            {
                if (game_state == 1)
                {
                    //1 render a new ball
                    if (new_ball_counter >= 20 - (speed_level + 3) * 3)
                    {
                        //add a new falling ball
                        falling_ball.Add(new Falling_Ball(rnd));
                        new_ball_counter = 0;

                    }
                    else
                    {
                        new_ball_counter++;
                    }

                    //2 moving balls and display
                    List<Falling_Ball> removeList = new List<Falling_Ball>();
                    foreach (Falling_Ball fb in falling_ball)
                    {
                        fb.Move((speed_level + 5) * 0.5);
                        if (fb.GetPosition_Y() > RenderHeight)
                        {
                            removeList.Add(fb);
                        }
                    }
                    //remove balls that touch the ground
                    foreach (Falling_Ball remove_fb in removeList)
                    {
                        falling_ball.Remove(remove_fb);
                    }
                }
                //draw balls
                Dispatcher.BeginInvoke(DispatcherPriority.Input, new ThreadStart(() =>
                {
                    using (DrawingContext dc2 = this.drawingGroup2.Open())
                    {
                        if (game_state == 1)
                        {
                            // Draw a transparent background to set the render size
                            dc2.DrawRectangle(Brushes.Transparent, null, new Rect(0.0, 0.0, RenderWidth, RenderHeight));
                            foreach (Falling_Ball fb in falling_ball)
                            {
                                Point point = new Point(fb.GetPosition_X(), fb.GetPosition_Y());
                                dc2.DrawEllipse(fb.GetColor(), null, point, fb.GetRadius(), fb.GetRadius());
                            }
                        }
                        if (game_timer == game_total_time)
                        {
                            command = "finish";
                        }
                        if (game_state == 1)
                            game_timer++;
                        if (command != "")
                        {
                            switch (command)
                            {
                                case "FINISH":
                                    if (game_state == 1)
                                    {
                                        game_state = 0;
                                        falling_ball.Clear();
                                        String winner;
                                        if (score1 > score2)
                                        {
                                            winner = "Player1 Win!";
                                            player1_win = true;
                                            player2_win = false;
                                        }
                                        else if (score1 < score2)
                                        {
                                            winner = "Player2 Win!";
                                            player2_win = true;
                                            player1_win = false;
                                        }
                                        else
                                        {
                                            winner = "Tie!";
                                            player1_win = true;
                                            player2_win = true;
                                        }
                                        command = "Time Up!" + winner;
                                    }
                                    break;
                                case "START":
                                    if (game_state == 0)
                                    {
                                        game_state = 1;
                                        new_ball_counter = 0;
                                        score1 = 0;
                                        score2 = 0;
                                        game_timer = 0;

                                    }
                                    else if (game_state == -1)
                                    {
                                        game_state = 1;
                                        new_ball_counter = 0;
                                        
                                    }
                                        
                                    break;
                                case "PAUSE":
                                    if (game_state == 1)
                                    {
                                        game_state = -1;
                                        falling_ball.Clear();
                                        if (score1 > score2)
                                        {

                                            player1_win = true;
                                            player2_win = false;
                                        }
                                        else if (score1 < score2)
                                        {
                                            player2_win = true;
                                            player1_win = false;
                                        }
                                        else
                                        {

                                            player1_win = true;
                                            player2_win = true;
                                        }
                                    }
                                    break;
                                case "SPEED_UP":
                                    if (speed_level < 3)
                                    {
                                        speed_level += 1;
                                    }
                                    command = "current speed: " + speed_level.ToString();
                                    break;
                                case "SLOW_DOWN":
                                    if (speed_level > -3)
                                    {
                                        speed_level -= 1;
                                    }
                                    command = "current speed: " + speed_level.ToString();
                                    break;
                            }

                            //display command
                            FormattedText text = new FormattedText(command,
                                                CultureInfo.GetCultureInfo("en-us"),
                                                FlowDirection.LeftToRight,
                                                new Typeface("Verdana"),
                                                20, System.Windows.Media.Brushes.Aqua);
                            text.TextAlignment=TextAlignment.Center;
                            dc2.DrawText(text,new System.Windows.Point(RenderWidth / 2, RenderHeight - 60));
                        }
                        if (game_state == 1)
                        {
                            //display timer
                            int game_second = (game_total_time- game_timer) / 20;
                            dc2.DrawText(
                                new FormattedText(game_second.ToString(),
                                CultureInfo.GetCultureInfo("en-us"),
                                FlowDirection.LeftToRight,
                                new Typeface("Verdana"),
                                20, System.Windows.Media.Brushes.Aqua),
                                new System.Windows.Point(RenderWidth / 2, 20));
                        }

                        //display score
                        dc2.DrawText(
                                new FormattedText("Score1:" + score1.ToString(),
                                CultureInfo.GetCultureInfo("en-us"),
                                FlowDirection.LeftToRight,
                                new Typeface("Verdana"),
                                20, System.Windows.Media.Brushes.Cyan),
                                new System.Windows.Point(20, 20));

                        dc2.DrawText(
                                new FormattedText("Score2:" + score2.ToString(),
                                CultureInfo.GetCultureInfo("en-us"),
                                FlowDirection.LeftToRight,
                                new Typeface("Verdana"),
                                20, System.Windows.Media.Brushes.Pink),
                                new System.Windows.Point(RenderWidth - 100, 20));

                    }
                    // prevent drawing outside of our render area
                    this.drawingGroup2.ClipGeometry = new RectangleGeometry(new Rect(0.0, 0.0, RenderWidth, RenderHeight));
                    //}
                }
                ));
            }
        }

        private bool IsHit_BallToBall(Point center1, Point center2, double threadHold)
        {
            double d;
            d = Math.Sqrt(Math.Pow((center1.X - center2.X), 2) + Math.Pow((center1.Y - center2.Y), 2));
            return d <= threadHold;
        }


        double GetDistance(Point center1, Point center2)
        {
            double d = Math.Sqrt(Math.Pow((center1.X - center2.X), 2) + Math.Pow((center1.Y - center2.Y), 2));
            return d;
        }

        private bool IsHit_LineToBall(Point point1, Point point2, Point center, double threadHold)
        {
            //corners
            if (GetDistance(point1, center) < threadHold)
                return true;
            if (GetDistance(point2, center) < threadHold)
                return true;
            return false;
            // line
            Point VectorA, VectorB;
            VectorA = new Point(point1.X - center.X, point1.Y - center.Y);
            VectorB = new Point(point1.X - point2.X, point1.Y - point2.Y);

            double dotPoduct;
            dotPoduct = VectorA.X * VectorB.X + VectorA.Y * VectorB.Y;

            double lengthA, lengthB;
            lengthA = Math.Sqrt(Math.Pow(VectorA.X, 2) + Math.Pow(VectorA.Y, 2));
            lengthB = Math.Sqrt(Math.Pow(VectorB.X, 2) + Math.Pow(VectorB.Y, 2));

            double theta;
            theta = Math.Acos((dotPoduct / lengthA) / lengthB);
            if (theta < 0) return false;

            double h;
            h = Math.Sin(theta) * lengthA;
            return h <= threadHold;

        }


        /// <summary>
        /// Execute shutdown tasks
        /// </summary>
        /// <param name="sender">object sending the event</param>
        /// <param name="e">event arguments</param>
        private void WindowClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (null != this.sensor)
            {
                this.sensor.Stop();
            }
        }


        /// <summary>
        /// Event handler for Kinect sensor's SkeletonFrameReady event
        /// </summary>
        /// <param name="sender">object sending the event</param>
        /// <param name="e">event arguments</param>
        private void SensorSkeletonFrameReady(object sender, SkeletonFrameReadyEventArgs e)
        {
            Skeleton[] skeletons = new Skeleton[0];

            using (SkeletonFrame skeletonFrame = e.OpenSkeletonFrame())
            {
                if (skeletonFrame != null)
                {
                    skeletons = new Skeleton[skeletonFrame.SkeletonArrayLength];
                    skeletonFrame.CopySkeletonDataTo(skeletons);
                }
                
            }

            using (DrawingContext dc = this.drawingGroup.Open())
            {
                // Draw a transparent background to set the render size
                dc.DrawRectangle(Brushes.Black, null, new Rect(0.0, 0.0, RenderWidth, RenderHeight));

                if (skeletons.Length != 0)
                {
                    id = 0;

                    for (int i = 0; i < skeletons.Length; i++)
                    {
                        Skeleton skel = skeletons[i];
                        //RenderClippedEdges(skel, dc);
                        if (skel.TrackingState == SkeletonTrackingState.Tracked)
                        {
                            this.DrawBonesAndJoints(skel, dc, id);
                            id = id + 1;
                        }
                        
                    }
                }

                // prevent drawing outside of our render area
                this.drawingGroup.ClipGeometry = new RectangleGeometry(new Rect(0.0, 0.0, RenderWidth, RenderHeight));
            }
        }

        /// <summary>
        /// Draws a skeleton's bones and joints
        /// </summary>
        /// <param name="skeleton">skeleton to draw</param>
        /// <param name="drawingContext">drawing context to draw to</param>
        private void DrawBonesAndJoints(Skeleton skeleton, DrawingContext drawingContext, int id)
        {
            // Render Torso
            this.DrawBone(id, skeleton, drawingContext, JointType.Head, JointType.ShoulderCenter);
            this.DrawBone(id, skeleton, drawingContext, JointType.ShoulderCenter, JointType.ShoulderLeft);
            this.DrawBone(id, skeleton, drawingContext, JointType.ShoulderCenter, JointType.ShoulderRight);
            this.DrawBone(id, skeleton, drawingContext, JointType.ShoulderCenter, JointType.Spine);
            this.DrawBone(id, skeleton, drawingContext, JointType.Spine, JointType.HipCenter);
            this.DrawBone(id, skeleton, drawingContext, JointType.HipCenter, JointType.HipLeft);
            this.DrawBone(id, skeleton, drawingContext, JointType.HipCenter, JointType.HipRight);

            // Left Arm
            this.DrawBone(id, skeleton, drawingContext, JointType.ShoulderLeft, JointType.ElbowLeft);
            this.DrawBone(id, skeleton, drawingContext, JointType.ElbowLeft, JointType.WristLeft);
            this.DrawBone(id, skeleton, drawingContext, JointType.WristLeft, JointType.HandLeft);

            // Right Arm
            this.DrawBone(id, skeleton, drawingContext, JointType.ShoulderRight, JointType.ElbowRight);
            this.DrawBone(id, skeleton, drawingContext, JointType.ElbowRight, JointType.WristRight);
            this.DrawBone(id, skeleton, drawingContext, JointType.WristRight, JointType.HandRight);

            // Left Leg
            this.DrawBone(id, skeleton, drawingContext, JointType.HipLeft, JointType.KneeLeft);
            this.DrawBone(id, skeleton, drawingContext, JointType.KneeLeft, JointType.AnkleLeft);
            this.DrawBone(id, skeleton, drawingContext, JointType.AnkleLeft, JointType.FootLeft);

            // Right Leg
            this.DrawBone(id, skeleton, drawingContext, JointType.HipRight, JointType.KneeRight);
            this.DrawBone(id, skeleton, drawingContext, JointType.KneeRight, JointType.AnkleRight);
            this.DrawBone(id, skeleton, drawingContext, JointType.AnkleRight, JointType.FootRight);

            // Render Joints

            foreach (Joint joint in skeleton.Joints)
            {
                Brush drawBrush = null;

                if (joint.TrackingState == JointTrackingState.Tracked)
                {
                    drawBrush = this.trackedJointBrush;
                }
                else if (joint.TrackingState == JointTrackingState.Inferred)
                {
                    drawBrush = this.inferredJointBrush;
                }
                double actualThickness;

                if (joint.JointType == JointType.HandLeft || joint.JointType == JointType.HandRight || joint.JointType == JointType.FootLeft || joint.JointType == JointType.FootRight)
                {
                    actualThickness = jointSize;
                }
                else { actualThickness = JointThickness; }

                if (drawBrush != null)
                {
                    drawingContext.DrawEllipse(drawBrush, null, this.SkeletonPointToScreen(joint.Position), actualThickness, actualThickness);
                }

                if (joint.JointType == JointType.Head || joint.JointType == JointType.HandLeft || joint.JointType == JointType.HandRight || joint.JointType == JointType.FootLeft || joint.JointType == JointType.FootRight)
                {
                    lock (falling_ball)
                    {
                        List<Falling_Ball> delete_ball = new List<Falling_Ball>();
                        foreach (Falling_Ball fb in falling_ball)
                        {
                            double threadhold;
                            if (joint.JointType == JointType.Head)
                                threadhold = headSize / 2;
                            else threadhold = jointSize;
                            //see if skeleton hit the correct ball, increment score
                            if (IsHit_BallToBall(new Point(fb.GetPosition_X(), fb.GetPosition_Y()), this.SkeletonPointToScreen(joint.Position), fb.GetRadius() + threadhold))
                            {
                                bool correctHit = (id == fb.GetColorInt());
                                int increment;
                                if (correctHit)
                                    increment = 1;
                                else
                                    increment = -1;
                                delete_ball.Add(fb);
                                if (id == 0)
                                    score1 += increment;
                                else
                                    score2 += increment;
                            }
                        }
                        foreach (Falling_Ball fb in delete_ball)
                        {
                            falling_ball.Remove(fb);
                        }

                    }
                }
            }

            //change head/face of the skeleton
            Point headPoint = this.SkeletonPointToScreen(skeleton.Joints[JointType.Head].Position);
            double HW = headSize;
            double HH = headSize;
            double HX = headPoint.X;
            double HY = headPoint.Y;
            String profile_picture;

            if (id % 2 == 0)
            {
                if (game_state == 0&&command!="hello")
                {
                    
                    if (player1_win)
                        profile_picture = @"../../Images/win.png";
                    else
                        profile_picture = @"../../Images/lost.png";
                }
                else
                    profile_picture = @"../../Images/happy-female.png";
            }
            else
            {
                if (game_state == 0 && command != "hello")
                {
                    if (player2_win)
                        profile_picture = @"../../Images/win.png";
                    else
                        profile_picture = @"../../Images/lost.png";
                }
                else
                profile_picture = @"../../Images/happy-male.png";
            }

            Rect rect_h = new Rect(HX - 0.5 * HW, HY - 0.5 * HH, HW, HH);
            BitmapImage imgSrc = new BitmapImage();
            imgSrc.BeginInit();
            imgSrc.UriSource = new Uri(profile_picture, UriKind.Relative);
            imgSrc.EndInit();

            drawingContext.DrawImage(imgSrc, rect_h);
        }

        /// <summary>
        /// Maps a SkeletonPoint to lie within our render space and converts to Point
        /// </summary>
        /// <param name="skelpoint">point to map</param>
        /// <returns>mapped point</returns>
        private Point SkeletonPointToScreen(SkeletonPoint skelpoint)
        {
            // Convert point to depth space.  
            // We are not using depth directly, but we do want the points in our 640x480 output resolution.
            DepthImagePoint depthPoint = this.sensor.CoordinateMapper.MapSkeletonPointToDepthPoint(skelpoint, DepthImageFormat.Resolution640x480Fps30);
            return new Point(depthPoint.X, depthPoint.Y);
        }

        /// <summary>
        /// Draws a bone line between two joints
        /// </summary>
        /// <param name="skeleton">skeleton to draw bones from</param>
        /// <param name="drawingContext">drawing context to draw to</param>
        /// <param name="jointType0">joint to start drawing from</param>
        /// <param name="jointType1">joint to end drawing at</param>
        private void DrawBone(int id, Skeleton skeleton, DrawingContext drawingContext, JointType jointType0, JointType jointType1)
        {
            Joint joint0 = skeleton.Joints[jointType0];
            Joint joint1 = skeleton.Joints[jointType1];

            // If we can't find either of these joints, exit
            if (joint0.TrackingState == JointTrackingState.NotTracked ||
                joint1.TrackingState == JointTrackingState.NotTracked)
            {
                return;
            }

            // Don't draw if both points are inferred
            if (joint0.TrackingState == JointTrackingState.Inferred &&
                joint1.TrackingState == JointTrackingState.Inferred)
            {
                return;
            }

            //// We assume all drawn bones are inferred unless BOTH joints are tracked
            //Pen drawPen = this.inferredBonePen;
            //if (joint0.TrackingState == JointTrackingState.Tracked && joint1.TrackingState == JointTrackingState.Tracked)
            //{
            //    drawPen = this.trackedBonePen;
            //}
            Pen drawPen;
            if (id == 0)
                drawPen = new Pen(Brushes.Blue, 6);
            else
                drawPen = new Pen(Brushes.Red, 6);
            drawingContext.DrawLine(drawPen, this.SkeletonPointToScreen(joint0.Position), this.SkeletonPointToScreen(joint1.Position));

            lock (falling_ball)
            {
                //test whether they will hit
                List<Falling_Ball> remove_fallingBall = new List<Falling_Ball>();

                foreach (Falling_Ball fb in falling_ball)
                {
                    if (IsHit_LineToBall(this.SkeletonPointToScreen(joint0.Position), this.SkeletonPointToScreen(joint1.Position), new Point(fb.GetPosition_X(), fb.GetPosition_Y()), fb.GetRadius()))
                    {

                        bool correctHit = (id == fb.GetColorInt());
                        int increment;
                        if (correctHit)
                            increment = 1;
                        else
                            increment = -1;
                        remove_fallingBall.Add(fb);
                        if (id == 0)
                            score1 += increment;
                        else
                            score2 += increment;
                    }

                }
                foreach (Falling_Ball fb in remove_fallingBall)
                {
                    falling_ball.Remove(fb);
                }
            }
        }

        /// <summary>
        /// Handles the checking or unchecking of the seated mode combo box
        /// </summary>
        /// <param name="sender">object sending the event</param>
        /// <param name="e">event arguments</param>
        private void CheckBoxSeatedModeChanged(object sender, RoutedEventArgs e)
        {
            if (null != this.sensor)
            {
                if (this.checkBoxSeatedMode.IsChecked.GetValueOrDefault())
                {
                    this.sensor.SkeletonStream.TrackingMode = SkeletonTrackingMode.Seated;
                }
                else
                {
                    this.sensor.SkeletonStream.TrackingMode = SkeletonTrackingMode.Default;
                }
            }
        }


    }
}