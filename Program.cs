using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using CommandLine;
using Discord;
using Newtonsoft.Json;

namespace NovarinRPCManager
{
    class Program
    {
        public static Process robloxGameProccess;
        private const long CLIENT_ID = 1353418280349470801; // Replace with your actual client ID
        public static RPCLaunchArgs launchArgs = new RPCLaunchArgs();

        static void Main(string[] args)
        {
            if (!Parser.Default.ParseArguments(args, launchArgs))
            {
                Console.WriteLine("Failed to parse arguments");
                return;
            }

            //Console.ReadLine();

            //System.Threading.Thread.Sleep(5000);

            try
            {
                robloxGameProccess = Process.GetProcessById(launchArgs.ProccessID);
                if (robloxGameProccess == null || robloxGameProccess.HasExited)
                {
                    Console.WriteLine("Failed to find Roblox process with ID: " + launchArgs.ProccessID);
                    return;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("Failed to find Roblox process with ID: " + launchArgs.ProccessID);
                return;
            }

            try
            {
                DoRPCOfPlace(robloxGameProccess, launchArgs.GameID, launchArgs.JobID, launchArgs.LaunchProtocol);
            }
            catch (Exception e)
            {
                Console.WriteLine("An error occurred: " + e.Message);
                Console.WriteLine(e.StackTrace);
                Console.ReadLine();
            }
        }

        public static void DoRPCOfPlace(Process robloxProcess, string placeId, string jobId, string launchProtocol)
        {
            var discord = new Discord.Discord(CLIENT_ID, (UInt64)Discord.CreateFlags.Default);

            // Initialize event handlers and other functionalities here
            discord.SetLogHook(LogLevel.Debug, (level, message) =>
            {
                Console.WriteLine($"Log[{level}]: {message}");
            });

            PlaceInfo place = GetPlaceInfo(placeId);
            if (place == null)
            {
                Console.WriteLine("Failed to fetch place info from the API");
                return;
            }
            if (place.Error != null)
            {
                Console.WriteLine("Failed to fetch place info from the API: " + place.Error);
                return;
            }

            PlayersInJob playersInJob = GetPlayersInJob(jobId);
            if (playersInJob == null)
            {
                Console.WriteLine("Failed to fetch player count from the API");
                return;
            }
            if (playersInJob.Error != null)
            {
                Console.WriteLine("Failed to fetch player count from the API: " + playersInJob.Error);
                return;
            }

            var activityManager = discord.GetActivityManager();
            activityManager.RegisterCommand(launchProtocol + "://discordrpc+");

            long startPlayTime = (long)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;

            int loopsTillPlayerCountCheck = 50;
            int failureStrikes = 0;
            // Main loop
            while (true)
            {
                
                if (failureStrikes >= 5)
                {
                    Console.WriteLine("Failed to update activity 5 times in a row, stopping the RPC");
                    return;
                }

                if (loopsTillPlayerCountCheck >= 50)
                {
                    loopsTillPlayerCountCheck = 0;
                    playersInJob = GetPlayersInJob(jobId);
                    if (playersInJob == null)
                    {
                        Console.WriteLine("Failed to fetch player count from the API");
                        failureStrikes++;
                        break;
                    }
                    if (playersInJob.Error != null)
                    {
                        Console.WriteLine("Failed to fetch player count from the API: " + playersInJob.Error);
                        if (playersInJob.Error == "Job does not exist")
                        {
                            Console.WriteLine("Job not found, stopping the RPC");
                            return;
                        }
                        failureStrikes++;
                        break;
                    }

                    activityManager.UpdateActivity(new Activity
                    {
                        State = "Playing",
                        Details = "Playing \"" + place.Name + "\"",
                        Timestamps =
                        {
                            Start = startPlayTime,
                        },
                        Assets =
                        {
                            LargeImage = "novarin_logo",
                            LargeText = "Novarin"
                        },
                        Party =
                        {
                            Id = $"{jobId}",
                            Size = {
                                CurrentSize = playersInJob.PlayerCount,
                                MaxSize = playersInJob.MaxPlayers,
                            },
                        },
                        Secrets =
                        {
                            Join = $"{placeId}+{jobId}",
                        },
                        Instance = true,
                    }, (res) =>
                    {
                        if (res == Discord.Result.Ok)
                        {
                            Console.WriteLine("Activity updated successfully");
                        }
                        else
                        {
                            Console.WriteLine($"Failed to update activity: {res}");
                        }
                    });

                    // We succeed so lets reset the failure count
                    failureStrikes = 0;
                }
                if (robloxProcess.HasExited)
                {
                    Console.WriteLine("Roblox process has exited, stopping the RPC");
                    return;
                }
                discord.RunCallbacks();
                System.Threading.Thread.Sleep(1000 / 5); // Run at 5 FPS
                loopsTillPlayerCountCheck++;
            }
        }

        private static string GetUserAgent()
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version.ToString();
            return $"NovarinRPC/{version}";
        }


        private static PlaceInfo GetPlaceInfo(string placeId)
        {
            // Fetch place info from the API
            try
            {
                using (WebClient client = new WebClient())
                {
                    client.Headers.Add("user-agent", GetUserAgent());
                    string receivedClientData = client.DownloadString("https://novarin.cc/marketplace/productinfo?assetId=" + placeId);
                    return JsonConvert.DeserializeObject<PlaceInfo>(receivedClientData);
                }
            }
            catch (Exception e)
            {
                return null;
            }
        }

        private static PlayersInJob GetPlayersInJob(string jobId)
        {
            // Fetch place info from the API
            try
            {
                using (WebClient client = new WebClient())
                {
                    client.Headers.Add("user-agent", GetUserAgent());
                    string receivedClientData = client.DownloadString("https://novarin.cc/app/api/games/playersInJob?jobid=" + jobId);
                    return JsonConvert.DeserializeObject<PlayersInJob>(receivedClientData);
                }
            }
            catch (Exception e)
            {
                return null;
            }
        }

    }

    class RPCLaunchArgs
    {
        [Option('g', "gameid", Required = true)]
        public string GameID { get; set; }
        [Option('j', "jobid", Required = true)]
        public string JobID { get; set; }
        [Option('l', "launchprotocol", Required = true)]
        public string LaunchProtocol { get; set; }
        [Option('p', "processid", Required = true)]
        public int ProccessID { get; set; }

    }

    class PlaceInfo
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public int TargetId { get; set; }
        public string Error { get; set; }
    }

    class PlayersInJob
    {
        public int PlayerCount { get; set; }
        public int MaxPlayers { get; set; }
        public string Error { get; set; }
    }
}
