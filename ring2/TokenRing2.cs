//This template is used to help you to build  
//the code structure when you have no idea about
//how to start your work. You can ignore it if 
//it's useless to you.

// Template for C# Project2 implementation.
// You may need extend this code for the
// monitor or priority extra credits, but this
// template should be a good starting point

// Your code may contain any part, including all,
// of this, but it must be COMMENTED much better!

using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Diagnostics;

// ip address of localhost
static class Address
{
    private static IPAddress ip = new IPAddress(new byte[] { 127, 0, 0, 1 });
    public static IPAddress IP { get { return ip; } }
}

class TokenRing
{
    static void Main(string[] argv)
    {
        int port;
        Node[] nodes;
        Thread[] threads;
        String curDir = Directory.GetCurrentDirectory();
        String[] inputFile;

        // Add another argument if you're doing the
        // priority extra credit
        if (argv.Length != 1)
        {
            Console.WriteLine("Usage: TokenRing <Conf file name>");
        }

        int numNodes = 0;

        inputFile = System.IO.File.ReadAllLines(curDir + "\\" + argv[0]);

        numNodes = inputFile.Length + 1; // +1 for monitor node
        nodes = new Node[numNodes];

        // use port range of 5005 to 5005+numNodes, port 5004 for monitor node
        port = 5005;

        // first create the monitor node
        nodes[0] = new Monitor(0, port, (byte)numNodes, Convert.ToByte(inputFile[inputFile.Length - 1]));
        port++;

        // create the nodes
        // <= numNodes because the first node is the monitor, 
        // and want the number of nodes input to be the number of active nodes
        for (byte i = 1; i < numNodes; i++)
        {
            nodes[i] = new Node(Convert.ToByte(inputFile[i - 1]), port);
            port++;
        }
        Console.WriteLine("Connect to this token ring on port " + port);

        threads = new Thread[numNodes + 1];

        // create a thread for each node.
        for (byte i = 0; i < numNodes; i++)
        {
            threads[i] = new Thread(new ThreadStart(nodes[i].Run));
            threads[i].Name = i.ToString();
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

        /*byte[] bytes = {10, 5, 10, 15};
        Frame test = new Frame(0, 1, 5, bytes);
        byte[] binary = test.ToBinary();
        Frame output = Frame.MakeFrame(binary);
        Console.Write(output.AC + " " + output.FC + " " + output.DA + " " + output.SA + " " + output.size + " ");
        for (int i = 0; i < bytes.Length; i++)
        {
            Console.Write(output.data[i] + ",");
        }
        Console.WriteLine(" " + output.FS);*/
    }
}

// the frame to send and recieve
class Frame
{
    public byte AC; // used for shutdown. Originally priority, but not implementing priority.
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

class Node
{
    // Choose a reasonable Token Holding Time
    // and describe your choice in your README
    protected int THT;
    protected byte nodeNum;
    protected int nodePort;

    protected TcpClient sendClient;
    protected NetworkStream sendStream;

    protected TcpClient recieveClient;
    protected NetworkStream recieveStream;

    // input and output streams
    String curDir;
    String[] inputFile;
    System.IO.StreamWriter outputFile;
    int index;

    public Node()
    {
        this.nodeNum = 0;
        this.nodePort = -1;
    }

    // create the node with its number and port
    public Node(byte num, int port)
    {
        this.nodeNum = num;
        this.nodePort = port;
        this.curDir = Directory.GetCurrentDirectory();
        this.index = 0;
    }

    // connect with neighboring nodes
    // connect to the next node with the port assigned, listen on the port-1
    public virtual void Connect()
    {
        TcpListener listener;
        sendClient = new TcpClient();
        recieveClient = new TcpClient();
        IPEndPoint endpoint = new IPEndPoint(Address.IP, (this.nodePort - 1)); // listen on port-1

        // if odd, listen for connection from the previous node first.
        if ((this.nodeNum % 2) != 0)
        {
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

        // if even, connect to the next node first.
        if ((this.nodeNum % 2) == 0)
        {
            // connect to node first
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

            // then listen
            listener = new TcpListener(endpoint);
            listener.Start();

            recieveClient = listener.AcceptTcpClient();
            this.recieveStream = recieveClient.GetStream();
        }
    }

    // what runs on startup of node
    public virtual void Run()
    {
        // connect with neighboring nodes
        this.Connect();
        Console.WriteLine(this.nodeNum + "Connected");

        // open all the files
        this.inputFile = System.IO.File.ReadAllLines(curDir + "\\input-file-" + this.nodeNum);
        this.outputFile = new System.IO.StreamWriter(curDir + "\\output-file-" + this.nodeNum);

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
                    // if there is more to send
                    if (inputFile.Length > (this.index))
                    {
                        Transmit(frame);
                        continue;
                    }
                    else
                    {
                        // otherwise, send the frame along
                        bFrame = frame.ToBinary();
                        this.sendStream.Write(bFrame, 0, bFrame.Length);
                    }
                }

                // shutdown signal.
                else if (frame.AC == 1)
                {
                    if (this.nodeNum != frame.DA)
                    {
                        bFrame = frame.ToBinary();
                        this.sendStream.Write(bFrame, 0, bFrame.Length);
                    }
                    break;
                }

                // else if it's for me, process it.
                else if (frame.DA == this.nodeNum)
                {
                    // Write it to the node's output-file-i
                    // generate a random number between 0 and 10
                    // if it is >5, FS=3, and the frame is not accepted
                    if (rand.Next(10) >= 5)
                    {
                        frame.FS = 3;
                        bFrame = frame.ToBinary();
                        sendStream.Write(bFrame, 0, bFrame.Length);
                        continue;
                    }

                    // otherwise, process the frame
                    frame.FS = 2;

                    // print to file
                    outputFile.WriteLine(frame.SA.ToString() + "," + frame.DA.ToString() + "," + frame.size.ToString() + "," + frame.getData());
                    //Console.WriteLine(this.nodeNum + " Recieved frame and processed");

                    // send ack
                    bFrame = frame.ToBinary();
                    this.sendStream.Write(bFrame, 0, bFrame.Length);

                    //this.outputFile.Close();
                    continue;
                }
                else
                {
                    // otherwise, send the frame along
                    bFrame = frame.ToBinary();
                    this.sendStream.Write(bFrame, 0, bFrame.Length);
                }
                //this.outputFile.Close();
            }
        }
        finally
        {
            // Close all open resources (i.e. sockets!)
            Console.WriteLine(this.nodeNum + " Exterminate!");
            this.recieveStream.Close();
            this.recieveClient.Close();
            this.outputFile.Close();
        }
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
        String[] splitInput;
        int dataSize;
        this.THT = 1040;
        Frame newToken;

        // send ack for token
        sendFrame = new Frame(0, token.SA, this.nodeNum, null);
        sendFrame.FS = 2;
        bFrame = sendFrame.ToBinary();
        sendStream.Write(bFrame, 0, bFrame.Length);

        // read from file, and build frame from it
        while (true)
        {
            // tokenize the input line
            splitInput = this.inputFile[index].Split(new char[] { ',' });
            dataSize = Convert.ToByte(splitInput[1]); // the size of data is always in the second array element
            if (dataSize > 254)
            {
                Console.WriteLine("LOL BAD STUFF");
            }

            if (this.THT < dataSize)
            {
                // not enough THT to send the frame, send a token
                newToken = new Token(0, 0, this.nodeNum);
                bFrame = newToken.ToBinary();

                sendStream.Write(bFrame, 0, bFrame.Length);
                recieveFrame = Frame.MakeFrame(Receive());
                Debug.Assert(recieveFrame.DA == this.nodeNum);
                Debug.Assert(recieveFrame.FS == 2 || recieveFrame.FS == 3);

                return;
            }

            // otherwise, build and send the frame
            sendFrame = new Frame(0, Convert.ToByte(splitInput[0]), this.nodeNum, new System.Text.UTF8Encoding().GetBytes(splitInput[2]));
            bFrame = sendFrame.ToBinary();
            sendStream.Write(bFrame, 0, bFrame.Length);
            THT -= sendFrame.size;

            // recieve ack
            while (true)
            {
                recieveFrame = Frame.MakeFrame(Receive());
                Debug.Assert(recieveFrame.SA == this.nodeNum);
                Debug.Assert(recieveFrame.DA == sendFrame.DA);
                Debug.Assert(recieveFrame.FS == 2 || recieveFrame.FS == 3);

                // frame wasn't accepted, resend
                if (recieveFrame.FS == 3)
                {
                    // is there enough THT to resend?
                    if (this.THT > sendFrame.size)
                    {
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
                        Debug.Assert(recieveFrame.DA == this.nodeNum);
                        Debug.Assert(recieveFrame.FS == 2 || recieveFrame.FS == 3);
                        return;
                    }
                }
                else
                {
                    // frame was successfully recieved, move onto the next one
                    this.index++;
                    if (this.index >= this.inputFile.Length)
                    {
                        // nothing more to send
                        newToken = new Token(0, 0, this.nodeNum);
                        bFrame = newToken.ToBinary();

                        sendStream.Write(bFrame, 0, bFrame.Length);
                        recieveFrame = Frame.MakeFrame(Receive());
                        Debug.Assert(recieveFrame.DA == this.nodeNum);
                        Debug.Assert(recieveFrame.FS == 2 || recieveFrame.FS == 3);
                        return;
                    }
                    break;
                }
            }
        }
    }
}

class Monitor : Node
{
    private byte ringSize;
    private byte lastNode;

    public Monitor(byte num, int port, byte size, byte last)
        : base(num, port)
    {
        this.ringSize = size;
        lastNode = last;
    }

    // override connect, connects to the last node of the list and the Monitor node of the list
    // works the same as the even node in normal connect method.
    public override void Connect()
    {
        TcpListener listener;
        IPEndPoint endpoint = new IPEndPoint(Address.IP, (this.nodePort + this.ringSize)); // listen on the last node's port

        // connect to node first
        while (true)
        {
            try
            {
                this.sendClient = new TcpClient("localhost", this.nodePort);
                break;
            }
            catch (SocketException)
            {
                Thread.Sleep(10);
                continue;
            }
        }
        this.sendStream = this.sendClient.GetStream();
        //Console.WriteLine("monitor node connected to next node on port " + this.nodePort);

        // then listen
        listener = new TcpListener(endpoint);
        listener.Start();

        this.recieveClient = listener.AcceptTcpClient();
        this.recieveStream = this.recieveClient.GetStream();
        //Console.WriteLine("Monitor node connected to last node on port " + endpoint.Port);
    }

    // monitors run, since it will only accept a token
    public override void Run()
    {
        // connect with neighboring nodes
        this.Connect();

        // Accept incoming connection and wait for
        // right neighbor to be ready.

        // create the first token
        Frame sendFrame = new Token(0, 0, 0);
        byte[] bFrame = sendFrame.ToBinary();
        Frame recieveFrame;
        Frame newToken;
        Frame frame;

        // send it!
        sendStream.Write(bFrame, 0, bFrame.Length);
        recieveFrame = Frame.MakeFrame(Receive());
        Debug.Assert(recieveFrame.DA == this.nodeNum);
        Debug.Assert(recieveFrame.FS == 2 || recieveFrame.FS == 3);

        try
        {
            while (true)
            {
                // listen state
                // recieve frame, check if token

                frame = Frame.MakeFrame(Receive());

                if (frame is Token)
                {
                    // if it was sent by the monitor, everyone is done sending
                    if (frame.SA == this.nodeNum)
                    {
                        // send a frame with the finished bit enabled
                        sendFrame = new Frame(2, 255, 0, null);
                        bFrame = sendFrame.ToBinary();
                        sendStream.Write(bFrame, 0, bFrame.Length);
                    }
                    // if the token wasn't sent by the monitor, must send ack
                    else
                    {
                        sendFrame = new Frame(0, frame.SA, this.nodeNum, null);
                        sendFrame.FS = 2;
                        bFrame = sendFrame.ToBinary();
                        sendStream.Write(bFrame, 0, bFrame.Length);
                    }

                    // otherwise send a new token if the SA is not the monitor
                    newToken = new Token(0, this.nodeNum, this.nodeNum);

                    bFrame = newToken.ToBinary();
                    sendStream.Write(bFrame, 0, bFrame.Length);
                    frame = Frame.MakeFrame(Receive());

                    if (frame.SA == this.nodeNum)
                    {
                        // send a frame with the kill bit enabled
                        sendFrame = new Frame(2, lastNode, 0, null);
                        bFrame = sendFrame.ToBinary();
                        sendStream.Write(bFrame, 0, bFrame.Length);
                    }
                    else
                    {
                        Debug.Assert(frame.DA == this.nodeNum);
                        Debug.Assert(frame.FS == 2 || frame.FS == 3);
                    }
                }
                // otherwise, check if the bridge is telling it to shutdown
                else if (frame.AC == 1)
                {
                    Console.WriteLine("Exterminate list");
                    // send a frame with the kill bit enabled
                    sendFrame = new Frame(1, lastNode, 0, null);
                    bFrame = sendFrame.ToBinary();
                    sendStream.Write(bFrame, 0, bFrame.Length);
                    break;
                }
                else
                {
                    bFrame = frame.ToBinary();
                    sendStream.Write(bFrame, 0, bFrame.Length);
                }
            }
        }
        finally
        {
            // Close all open resources (i.e. sockets!)
            Console.WriteLine("Monitor node EX-TERM-INATED!");
            this.recieveStream.Close();
            this.recieveClient.Close();
        }

    }

}