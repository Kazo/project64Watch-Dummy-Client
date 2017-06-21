using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace project64Watch_Dummy_Client
{
    class Program
    {
        static TcpClient tcpClient = new TcpClient();
        static String host = "127.0.0.1";
        static Int32 port = 6520;
        static String BatFile = "restart.bat";
        static UInt32 pMagic = 0x34364A50;//"PJ64"
        static UInt32 pCommand;
        static Random rand = new Random();
        static Byte[] BattleTeams;
        static Byte[] BattleText;
        static UInt32 PingTimer;

        static void Main(string[] args)
        {
            for (int i = 0; i < args.Length; i += 2)
            {
                if (args[i] == "-ip")
                {
                    host = args[i + 1];
                }

                if (args[i] == "-p")
                {
                    port = Convert.ToInt32(args[i + 1]);
                }

                if (args[i] == "-f")
                {
                    BatFile = args[i + 1];
                }
            }

            Connect();

            Thread thread = new Thread(Ping);
            thread.Start();

            while (true)
            {
                while (Available() == 0x00)
                {
                    PingTimer++;
                    Thread.Sleep(1);
                }
                PingTimer = 0;
                Byte[] Reply = new Byte[Available()];
                ReadPacket(Reply);
                if (BitConverter.ToUInt32(Reply, 0x00) == 0x34364A50)
                {
                    pCommand = ((BitConverter.ToUInt32(Reply, 0x04) & 0xFF000000) >> 0x18);
                    switch (pCommand)
                    {
                        case 0x00://Hello
                            {
                                //Console.WriteLine("Hello");
                                MakeTeams();
                            }
                            break;

                        case 0x01://Team Update
                            {
                                //Console.WriteLine("Team Update");
                                BattleTeams = GetData(Reply);
                            }
                            break;

                        case 0x02://Battle Text
                            {
                                //Console.WriteLine("Battle Text");
                                BattleText = GetData(Reply);
                                Console.WriteLine(Encoding.UTF8.GetString(BattleText, 0x00, 0x80).Replace("\0", ""));
                                if (BattleText[0x80] != 0x00)
                                {
                                    Console.WriteLine(Encoding.UTF8.GetString(BattleText, 0x80, 0x80).Replace("\0", ""));
                                }
                                if (BattleText[0x100] != 0x00)
                                {
                                    Console.WriteLine(Encoding.UTF8.GetString(BattleText, 0x100, 0x80).Replace("\0", ""));
                                }
                            }
                            break;

                        case 0x03://Battle Result
                            {
                                if ((BitConverter.ToUInt32(Reply, 0x04) & 0x01) == 0x00)
                                {
                                    Console.WriteLine("\n1P Wins!\n");
                                }
                                else
                                {
                                    Console.WriteLine("\n2P Wins!\n");
                                }
                                MakeTeams();
                            }
                            break;

                        case 0x07://Ping
                            {
                                //Ignore
                            }
                            break;
                    }
                }
            }
        }

        static void MakeTeams()
        {
            Byte[] TeamData = new Byte[0x60 * 3];
            Byte[] PokemonData;
            String[] FilePaths = Directory.GetFiles("SSD2", "*.pk2", SearchOption.AllDirectories);
            do
            {
                for (int i = 0; i < 3; i++)
                {
                    PokemonData = File.ReadAllBytes(FilePaths[rand.Next(FilePaths.Length)]);
                    Array.Copy(PokemonData, 0x00, TeamData, (0x60 * i), 0x60);
                }
            } while ((TeamData[0x00] == TeamData[0x60]) || (TeamData[0x00] == TeamData[0xC0]) || (TeamData[0x60] == TeamData[0xC0]));//No Same Pokémons
            SendTeam(0, TeamData);

            do
            {
                for (int i = 0; i < 3; i++)
                {
                    PokemonData = File.ReadAllBytes(FilePaths[rand.Next(FilePaths.Length)]);
                    Array.Copy(PokemonData, 0x00, TeamData, (0x60 * i), 0x60);
                }
            } while ((TeamData[0x00] == TeamData[0x60]) || (TeamData[0x00] == TeamData[0xC0]) || (TeamData[0x60] == TeamData[0xC0]));//No Same Pokémons
            SendTeam(1, TeamData);
        }

        static void Connect()
        {
            UInt32 ConnectCounter = 0;
            UInt32 RetryCounter = 0;

            tcpClient = new TcpClient();

            while (!tcpClient.Connected)
            {
                try
                {
                    Thread.Sleep(1);
                    ConnectCounter++;
                    if (ConnectCounter > 500)
                    {
                        if (RetryCounter < 2)
                        {
                            //Console.WriteLine("Connecting...");
                            RetryCounter++;
                            tcpClient.Connect(host, port);
                            Thread.Sleep(1000);
                        }
                        else
                        {
                            //Console.WriteLine("Starting...");
                            StartPJ64();
                            Thread.Sleep(1000);
                            RetryCounter = 0;
                        }
                        ConnectCounter = 0;
                    }
                }
                catch (Exception)
                {
                    tcpClient.Close();
                    tcpClient = new TcpClient();
                }
            }
        }

        private static Byte[] GetData(Byte[] Reply)
        {
            Byte[] result = new Byte[0];
            if (BitConverter.ToUInt32(Reply, 0x00) == 0x34364A50)
            {
                UInt32 pSize = BitConverter.ToUInt32(Reply, 0x08);

                if ((pSize > 0) && (Reply.Length >= pSize + 0x0C))
                {
                    result = new Byte[pSize];
                    Array.Copy(Reply, 0x0C, result, 0x00, pSize);
                }
            }
            return result;
        }

        static void SendTeam(UInt32 player, Byte[] Data)
        {
            UInt32 sCommand = (player + 0x05 << 0x18);
            Byte[] sBuffer = new Byte[Data.Length + 0x0C];
            Array.Copy(BitConverter.GetBytes(pMagic), 0x00, sBuffer, 0x00, 0x04);
            Array.Copy(BitConverter.GetBytes(sCommand), 0x00, sBuffer, 0x04, 0x04);
            Array.Copy(BitConverter.GetBytes(Data.Length), 0x00, sBuffer, 0x08, 0x04);
            Array.Copy(Data, 0x00, sBuffer, 0x0C, Data.Length);
            SendPacket(sBuffer);
        }

        static void SendPacket(Byte[] sBuffer)
        {
            try
            {
                tcpClient.GetStream().Write(sBuffer, 0, sBuffer.Length);
            }
            catch (Exception)
            {

            }
        }

        static void ReadPacket(Byte[] Reply)
        {
            try
            {
                tcpClient.GetStream().Read(Reply, 0, Reply.Length);
            }
            catch (Exception)
            {

            }
        }

        static int Available()
        {
            try
            {
                return tcpClient.Available;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        static void Ping()
        {
            while(true)
            {
                Thread.Sleep(500);
                if (PingTimer >= 2000)
                {
                    //Console.WriteLine("Reconnecting...");
                    StartPJ64();
                    Thread.Sleep(1000);
                    Connect();
                    PingTimer = 0;
                }
                else if (PingTimer >= 1000)
                {
                    //Console.WriteLine("Pinging...");
                    UInt32 sCommand = (0x07 << 0x18);
                    Byte[] sBuffer = new Byte[0x0C];
                    Array.Copy(BitConverter.GetBytes(pMagic), 0x00, sBuffer, 0x00, 0x04);
                    Array.Copy(BitConverter.GetBytes(sCommand), 0x00, sBuffer, 0x04, 0x04);
                    SendPacket(sBuffer);
                }
            }
        }

        static void StartPJ64()
        {
            Process myProcess = Process.Start(BatFile);
        }
    }
}