/* TODO:
 *      finish shutdown logic for bridge
 *      change monitor nodes to send other shutdown signal at first
 *      when monitor receives true shutdown signal, send one as before.
 *      add in log file events to bridge
 *      Optional: change frame makeup on 1 ring
 *      README
 */

using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Diagnostics;
using System.Collections;
using System.Collections.Generic;

class Start
{
    static void Main(string[] argv)
    {
        Node[] bridge = new Node[2];
        Thread[] threads = new Thread[2];
        int port;

        if (argv.Length != 1)
        {
            Console.WriteLine("Usage: Bridge <Log file name>");
            return;
        }

        Console.Write("Please input the connection info for the first token ring (printed at runtime): ");
        port = Convert.ToInt32(Console.ReadLine());
        bridge[0] = new Node(255, port, 1, argv[0]);

        Console.Write("Please input the connection info for the second token ring (printed at runtime): ");
        port = Convert.ToInt32(Console.ReadLine());
        bridge[1] = new Node(255, port, 2, argv[0]);

        for (byte i = 0; i < 2; i++)
        {
            threads[i] = new Thread(new ThreadStart(bridge[i].Run));
            threads[i].Name = "Bridge Thread " + i;
            try
            {
                threads[i].Start();
            }
            catch (Exception)
            {
                Console.WriteLine("Cannot start thread for node " + i + ".");
                Environment.Exit(1);
            }
        }
    }
}


class Bridge
{
    // frame buffers for each ring
    protected static Queue<Frame> buffer1;
    protected static Queue<Frame> buffer2;
    protected static Hashtable lookupTable; // key is the node number, and value is the list it is in.
    protected static bool shouldShutdown; // whether 1 side of the bridge is ready to shutdown

    // for locking out parts of buffer access by threads
    protected static Object buf1Locker = new Object();
    protected static Object buf2Locker = new Object();
    protected static Object lookupTableLocker = new Object();
    protected static Object shutdownLocker = new Object();

    public Bridge()
    {
        buffer1 = new Queue<Frame>();
        buffer2 = new Queue<Frame>();
        lookupTable = new Hashtable();
    }

    // all of this was for extra credit i ran out of time for
    // turns a frame from ring1 to a frame from ring2
    /*private byte[] frame1To2(byte[] frame)
    {

    }

    // turns a frame from ring2 to a frame from  ring1
    private byte[] frame2To1(byte[] frame)
    {

    }*/
}

// the frame to send and recieve
class Frame
{
    // used for shutdown if it == 1,
    // used to show 1 ring is ready to shutdown if it == 2
    // or used to show the bridge needs to add it to table if == 4.
    public byte AC;
    public byte FC; // frame control, 0 for token, 1 if frame
    public byte DA; // destination address (node number)
    public byte SA; // source address (node number)
    public byte size; // size of data portion, can span multiple frames.
    public byte FS; // set for whether frame is accepted or rejected
    public byte[] data; // data, size of each must be <=254

    // construct a basic frame
    public Frame(byte AC, byte DA, byte SA, byte[] data)
    {
        this.AC = AC;
        this.DA = DA;
        this.SA = SA;
        if (data == null)
        {
            this.data = new byte[0];
            this.size = 0;
        }
        else
        {
            this.data = data;
            this.size = (byte)data.Length;
        }
        this.FC = 1;
        this.FS = 0;
    }

    // make a frame object from a binary frame
    public static Frame MakeFrame(byte[] rep)
    {
        Frame frame;

        // if the frame is a token
        if (rep[1] == 0)
        {
            frame = new Token(rep[0], rep[2], rep[3]);
            frame.size = 0;
            frame.data = null;
            frame.FS = rep[5];
            return frame;
        }

        // otherwise, process the frame
        byte[] data = new byte[254];
        byte size = rep[4];
        // if there is data, copy it into a temporary array
        Array.Copy(rep, 5, data, 0, size);

        // create a new frame will all the data from the binary frame
        frame = new Frame(rep[0], rep[2], rep[3], data);
        frame.FC = rep[1];
        frame.FS = rep[5 + size];
        frame.size = size;

        return frame;
    }

    // get just the data that is needed, no extra
    public string getData()
    {
        byte[] tempData;
        int i;

        // figure out how many bytes are in the array
        for (i = 0; i < 254; i++)
        {
            if (data[i] == 0)
            {
                break;
            }
        }
        // make a new array of that length
        tempData = new byte[i];
        // copy only the data into the new array
        Array.Copy(data, tempData, i);

        // return the data as a string
        return new System.Text.UTF8Encoding().GetString(tempData);
    }

    // make a binary frame from a frame object
    public byte[] ToBinary()
    {
        MemoryStream ms = new MemoryStream(260);

        ms.WriteByte(this.AC);
        ms.WriteByte(this.FC);
        ms.WriteByte(this.DA);
        ms.WriteByte(this.SA);
        ms.WriteByte(this.size);
        if (this.data != null)
            ms.Write(this.data, 0, this.size);
        ms.WriteByte(this.FS);

        return ms.ToArray();
    }
}

class Token : Frame
{
    public Token(byte AC, byte DA, byte SA)
        : base(AC, DA, SA, null)
    {
        this.FC = 0;
    }
}
// extends bridge, so it has all the bridge functionality as a node
class Node : Bridge
{
    protected int THT;
    protected byte nodeNum;
    protected int nodePort;
    protected int bridgeNumber;

    protected TcpClient sendClient;
    protected NetworkStream sendStream;

    protected TcpClient recieveClient;
    protected NetworkStream recieveStream;

    protected static System.IO.StreamWriter logFile;
    protected static Object logLocker = new Object();

    // for logfile
    protected String curDir;

    public Node()
    {
        this.nodeNum = 0;
        this.nodePort = -1;
    }

    // create the node with its number and port
    public Node(byte num, int port, int bridgeNumber, string logfile)
        : base()
    {
        this.nodeNum = num;
        this.nodePort = port;
        this.curDir = Directory.GetCurrentDirectory();
        this.bridgeNumber = bridgeNumber;
        // so the log file is only created once
        if (bridgeNumber == 1)
        {
            logFile = new System.IO.StreamWriter(curDir + "\\" + logfile);
        }
    }

    // what runs on startup of node
    public virtual void Run()
    {
        int destRing = -1;
        bool shouldTransmit = false;
        bool shutdown;

        // connect with neighboring nodes
        this.Connect();
        Console.WriteLine(this.nodeNum + "Connected");
        lock (logLocker)
        {
            logFile.WriteLine(DateTime.Now.ToString() + " Connected with token ring " + this.bridgeNumber);
        }

        Random rand = new Random();

        byte[] bFrame;

        // Accept incoming connection and wait for
        // right neighbor to be ready.
        try
        {
            while (true)
            {
                // listen state
                // recieve frame, check if token
                Frame frame = Frame.MakeFrame(Receive());

                if (frame is Token)
                {
                    // check to see if the thread's buffer has something to transmit
                    if (bridgeNumber == 1)
                    {
                        lock (buf1Locker)
                        {
                            if (buffer1.Count != 0)
                                shouldTransmit = true;
                            else
                                shouldTransmit = false;
                        }
                    }
                    else
                    {
                        lock (buf2Locker)
                        {
                            if (buffer2.Count != 0)
                                shouldTransmit = true;
                            else
                                shouldTransmit = false;
                        }
                    }

                    // if it does, enter transmit state
                    if (shouldTransmit)
                    {
                        Transmit(frame);
                    }

                    else
                    {
                        // otherwise, send the token along
                        bFrame = frame.ToBinary();
                        this.sendStream.Write(bFrame, 0, bFrame.Length);
                    }
                }

                // shutdown signal.
                else if (frame.AC == 2 || frame.DA == 255)
                {
                    Console.WriteLine(this.bridgeNumber + " is ready to shutdown");
                    lock (logLocker)
                    {
                        logFile.WriteLine(DateTime.Now.ToString() + " Token ring " + this.bridgeNumber + " is ready to shutdown");
                    }
                    // figure out if the other side is ready to shutdown
                    lock (shutdownLocker)
                    {
                        shutdown = shouldShutdown;
                        if (!shouldShutdown)
                            shouldShutdown = true;
                    }

                    // if not ready to shutdown, go into infinite send state until a shutdown signal comes through the buffer
                    if (!shutdown)
                    {
                        while (true)
                        {
                            // check to see if the thread's buffer has something to transmit
                            if (bridgeNumber == 1)
                            {
                                lock (buf1Locker)
                                {
                                    if (buffer1.Count != 0)
                                        shouldTransmit = true;
                                    else
                                        shouldTransmit = false;
                                }
                            }
                            else
                            {
                                lock (buf2Locker)
                                {
                                    if (buffer2.Count != 0)
                                        shouldTransmit = true;
                                    else
                                        shouldTransmit = false;
                                }
                            }

                            if (shouldTransmit)
                            {
                                if (shutdownTransmit())
                                {
                                    break; // shutdown
                                }
                                else
                                    Thread.Sleep(10); // otherwise, sleep for 10 ms
                            }
                            else
                            {
                                Thread.Sleep(10); // if nothing in the queue, sleep and try again
                            }
                        }
                        break;
                    }
                    // otherwise, send shutdown signal to both rings and shutdown
                    else
                    {
                        frame = new Frame(1, 0, 255, null);
                        if (bridgeNumber == 1)
                        {
                            lock (buf2Locker)
                            {
                                buffer2.Enqueue(new Frame(frame.AC, frame.DA, frame.SA, frame.data));
                            }
                            bFrame = frame.ToBinary();
                            this.sendStream.Write(bFrame, 0, bFrame.Length);
                        }
                        else
                        {

                            lock (buf1Locker)
                            {
                                buffer1.Enqueue(new Frame(frame.AC, frame.DA, frame.SA, frame.data));
                            }
                            bFrame = frame.ToBinary();
                            this.sendStream.Write(bFrame, 0, bFrame.Length);
                        }
                    }
                    break;
                }

                // send the frame to the proper ring
                else
                {
                    // check for which ring the frame goes to
                    lock (lookupTableLocker)
                    {
                        if (lookupTable.Contains(frame.DA))
                            destRing = (int)lookupTable[frame.DA];
                        else
                            destRing = -1;
                    }
                    // if it is unknown, add it to both buffers
                    if (destRing == -1)
                    {
                        // set the bit to be checked later for adding to lookup table
                        frame.AC = 4;

                        // add it to both buffers
                        lock (buf1Locker)
                        {
                            buffer1.Enqueue(new Frame(frame.AC, frame.DA, frame.SA, frame.data));
                        }
                        lock (buf2Locker)
                        {
                            buffer2.Enqueue(new Frame(frame.AC, frame.DA, frame.SA, frame.data));
                        }
                        // setup "faked" ack
                        frame.FS = 2; // frame accepted
                        frame.AC = 0; // reset the AC bit to avoid confusion
                    }
                    // if it is known, add it to that buffer
                    else
                    {
                        if (destRing == 1)
                        {
                            lock (buf1Locker)
                            {
                                buffer1.Enqueue(new Frame(frame.AC, frame.DA, frame.SA, frame.data));
                            }
                            if (this.bridgeNumber != destRing)
                            {
                                lock (logLocker)
                                {
                                    logFile.WriteLine(DateTime.Now.ToString() + " Sending frame to Token Ring " + destRing + ". Source=" + frame.SA + " Destination=" + frame.DA);
                                }
                            }
                        }
                        else
                        {
                            lock (buf2Locker)
                            {
                                buffer2.Enqueue(new Frame(frame.AC, frame.DA, frame.SA, frame.data));
                            }
                            if (this.bridgeNumber != destRing)
                            {
                                lock (logLocker)
                                {
                                    logFile.WriteLine(DateTime.Now.ToString() + " Sending frame to Token Ring " + destRing + ". Source=" + frame.SA + " Destination=" + frame.DA);
                                }
                            }
                        }

                        // setup "faked" ack
                        frame.FS = 2;
                    }
                    // send ack
                    bFrame = frame.ToBinary();
                    this.sendStream.Write(bFrame, 0, bFrame.Length);
                }
            }
        }
        finally
        {
            // Close all open resources (i.e. sockets!)
            Console.WriteLine(this.nodeNum + " " + this.bridgeNumber + " Exterminate!");
            lock (logLocker)
            {
                logFile.WriteLine(DateTime.Now.ToString() + " Token ring " + this.bridgeNumber + " is shutting down");
            }
            if (this.bridgeNumber == 1)
            {
                Thread.Sleep(20);
                lock (logLocker)
                {
                    logFile.Close();
                }
            }
            this.recieveStream.Close();
            this.recieveClient.Close();
        }
    }

    // returns true if it needs to shutdown
    private bool shutdownTransmit()
    {
        // Send until THT reached
        // first send the token ack
        Frame sendFrame;
        Frame recieveFrame;
        byte[] bFrame;
        int dataSize;
        Queue<Frame> sendQueue;
        Object locker;

        // setup which buffer to send from
        if (bridgeNumber == 1)
        {
            lock (buf1Locker)
            {
                sendQueue = buffer1;
                locker = buf1Locker;
            }
        }
        else
        {
            lock (buf2Locker)
            {
                sendQueue = buffer2;
                locker = buf1Locker;
            }
        }

        // read from buffer and send it
        while (true)
        {
            // get the next frame to send and its size
            lock (locker)
            {
                sendFrame = sendQueue.Peek();
            }
            dataSize = sendFrame.size;

            // because nothing else needs to send, don't worry about THT
            lock (locker)
            {
                // nothing to send anymore, return
                if (sendQueue.Count == 0)
                {
                    return false;
                }
                // is it time to shutdown?
                else if (sendQueue.Peek().AC == 1)
                {
                    // send the frame and shutdown
                    sendFrame = sendQueue.Dequeue();
                    bFrame = sendFrame.ToBinary();
                    sendStream.Write(bFrame, 0, bFrame.Length);
                    THT -= sendFrame.size;
                    return true;
                }
            }

            // otherwise, send the frame
            bFrame = sendFrame.ToBinary();
            sendStream.Write(bFrame, 0, bFrame.Length);
            THT -= sendFrame.size;

            // recieve ack
            while (true)
            {
                recieveFrame = Frame.MakeFrame(Receive());
                //Debug.Assert(recieveFrame.SA == this.nodeNum);
                //Debug.Assert(recieveFrame.DA == sendFrame.DA);
                //Debug.Assert(recieveFrame.FS == 2 || recieveFrame.FS == 3);                

                // frame wasn't accepted, resend
                if (recieveFrame.FS == 3)
                {
                    // send it!
                    sendStream.Write(bFrame, 0, bFrame.Length);
                    THT -= sendFrame.size;
                    continue;
                }
                else
                {
                    // frame was successfully recieved, move onto the next one
                    lock (locker)
                    {
                        sendQueue.Dequeue();
                        if (sendQueue.Count == 0)
                        {
                            return false;
                        }
                    }
                    break;
                }
            }
        }
    }

    // connect with neighboring nodes
    // connect to the next node with the port assigned, listen on the port-1
    public void Connect()
    {
        TcpListener listener;
        sendClient = new TcpClient();
        recieveClient = new TcpClient();
        IPEndPoint endpoint = new IPEndPoint(Address.IP, (this.nodePort - 1)); // listen on port-1

        // listen for connection from the previous node first.
        // first listen
        listener = new TcpListener(endpoint);
        listener.Start();

        recieveClient = listener.AcceptTcpClient();
        this.recieveStream = recieveClient.GetStream();

        // then connect to next node
        while (true)
        {
            try
            {
                sendClient = new TcpClient("localhost", this.nodePort);
                break;
            }
            catch (SocketException)
            {
                Thread.Sleep(10);
                continue;
            }
        }
        this.sendStream = sendClient.GetStream();
    }



    // recieve a frame
    protected byte[] Receive()
    {
        // Read a complete frame off the wire

        byte[] recieved = new byte[260]; // 260 is max size of frame

        // read all of the first bytes
        recieved[0] = (byte)this.recieveStream.ReadByte();
        recieved[1] = (byte)this.recieveStream.ReadByte();
        recieved[2] = (byte)this.recieveStream.ReadByte();
        recieved[3] = (byte)this.recieveStream.ReadByte();
        recieved[4] = (byte)this.recieveStream.ReadByte();

        // read the data
        this.recieveStream.Read(recieved, 5, recieved[4]);

        // read the last byte
        recieved[5 + recieved[4]] = (byte)this.recieveStream.ReadByte();

        return recieved;
    }

    protected void Transmit(Frame token)
    {
        // Send until THT reached
        // first send the token ack
        Frame sendFrame;
        Frame recieveFrame;
        byte[] bFrame;
        int dataSize;
        this.THT = 1040;
        Frame newToken;
        Queue<Frame> sendQueue;
        Object locker;

        // send ack for token
        sendFrame = new Frame(0, token.SA, this.nodeNum, null);
        sendFrame.FS = 2;
        bFrame = sendFrame.ToBinary();
        sendStream.Write(bFrame, 0, bFrame.Length);

        // setup which buffer to send from
        if (bridgeNumber == 1)
        {
            lock (buf1Locker)
            {
                sendQueue = buffer1;
                locker = buf1Locker;
            }
        }
        else
        {
            lock (buf2Locker)
            {
                sendQueue = buffer2;
                locker = buf1Locker;
            }
        }

        // read from buffer and send it
        while (true)
        {
            // get the next frame to send and its size
            lock (locker)
            {
                sendFrame = sendQueue.Peek();
            }
            dataSize = sendFrame.size;

            if (this.THT < dataSize)
            {
                // not enough THT to send the frame, send a token
                newToken = new Token(0, 0, this.nodeNum);
                bFrame = newToken.ToBinary();

                sendStream.Write(bFrame, 0, bFrame.Length);
                recieveFrame = Frame.MakeFrame(Receive());
                //Debug.Assert(recieveFrame.DA == this.nodeNum);
                Debug.Assert(recieveFrame.FS == 2 || recieveFrame.FS == 3);

                return;
            }

            // otherwise, send the frame
            bFrame = sendFrame.ToBinary();
            sendStream.Write(bFrame, 0, bFrame.Length);
            THT -= sendFrame.size;

            // recieve ack
            while (true)
            {
                recieveFrame = Frame.MakeFrame(Receive());
                //Debug.Assert(recieveFrame.SA == this.nodeNum);
                //Debug.Assert(recieveFrame.DA == sendFrame.DA);
                //Debug.Assert(recieveFrame.FS == 2 || recieveFrame.FS == 3);

                // if it needs to be setup into the routing table
                if (recieveFrame.AC == 4)
                {
                    // if the frame recieved doesn't have the FS changed, nothing processed it
                    if (recieveFrame.FS == 0)
                    {
                        lock (locker)
                        {
                            sendQueue.Dequeue();
                            if (sendQueue.Count == 0)
                            {
                                // nothing more to send
                                newToken = new Token(0, 0, this.nodeNum);
                                bFrame = newToken.ToBinary();

                                sendStream.Write(bFrame, 0, bFrame.Length);
                                recieveFrame = Frame.MakeFrame(Receive());
                                //Debug.Assert(recieveFrame.DA == this.nodeNum);
                                Debug.Assert(recieveFrame.FS == 2 || recieveFrame.FS == 3);
                                return;
                            }
                        }
                        break;
                    }
                    // otherwise, add it to the routing table
                    else
                    {
                        lock (lookupTableLocker)
                        {
                            if (!lookupTable.Contains(sendFrame.DA))
                            {
                                lookupTable.Add(sendFrame.DA, bridgeNumber);
                                lock (logLocker)
                                {
                                    logFile.WriteLine(DateTime.Now.ToString() + " " + sendFrame.DA + " added to the routting table");
                                }
                            }
                        }

                        // make sure that if it needs to be resent, it doesn't get added again
                        sendFrame.AC = 0;
                        break;
                    }
                }

                // frame wasn't accepted, resend
                else if (recieveFrame.FS == 3)
                {
                    // is there enough THT to resend?
                    if (this.THT > sendFrame.size)
                    {
                        lock (logLocker)
                        {
                            //logFile.WriteLine(DateTime.Now.ToString() + " resending frame to " + sendFrame.DA + " from " + sendFrame.SA);
                        }
                        // send it!
                        sendStream.Write(bFrame, 0, bFrame.Length);
                        THT -= sendFrame.size;
                        continue;
                    }
                    else
                    {
                        // send the token along
                        newToken = new Token(0, 0, this.nodeNum);
                        bFrame = newToken.ToBinary();

                        sendStream.Write(bFrame, 0, bFrame.Length);
                        recieveFrame = Frame.MakeFrame(Receive());
                        //Debug.Assert(recieveFrame.DA == this.nodeNum);
                        Debug.Assert(recieveFrame.FS == 2 || recieveFrame.FS == 3);
                        return;
                    }
                }
                else
                {
                    // frame was successfully recieved, move onto the next one
                    lock (locker)
                    {
                        sendQueue.Dequeue();
                        if (sendQueue.Count == 0)
                        {
                            // nothing more to send
                            newToken = new Token(0, 0, this.nodeNum);
                            bFrame = newToken.ToBinary();

                            sendStream.Write(bFrame, 0, bFrame.Length);
                            recieveFrame = Frame.MakeFrame(Receive());
                            //Debug.Assert(recieveFrame.DA == this.nodeNum);
                            Debug.Assert(recieveFrame.FS == 2 || recieveFrame.FS == 3);
                            return;
                        }
                    }
                    break;
                }
            }
        }
    }
}

// ip address of localhost
static class Address
{
    private static IPAddress ip = new IPAddress(new byte[] { 127, 0, 0, 1 });
    public static IPAddress IP { get { return ip; } }
}
