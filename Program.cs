using System;
using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using CommandLine;
using Discord;
using Newtonsoft.Json;

namespace NovarinRPCManager
{
	class Program
	{
		public static Process robloxGameProccess;
		private const long CLIENT_ID = 1353418280349470801;
		public static RPCLaunchArgs launchArgs = new RPCLaunchArgs();

		[DllImport("user32.dll", CharSet = CharSet.Unicode)]
		public static extern int MessageBox(IntPtr h, string m, string c, int type);

		public static void Log(string type, string message)
		{
			Console.WriteLine($"[{DateTime.Now} | {type}] {message}");
			// MessageBox((IntPtr)0, message, $"Novarin RPC Manager | [{DateTime.Now} | {type}]", 0);
		}


		static void Main(string[] args)
		{
			if (!Parser.Default.ParseArguments(args, launchArgs))
			{
				Log("ERROR", "Failed to parse arguments!");
				return;
			}

			if (launchArgs.OpenedByDiscord)
			{
				Log("INFO", "Opened by Discord, waiting for join info callback...");

				DateTime startTime = DateTime.UtcNow;

				Discord.Discord discord = new Discord.Discord(CLIENT_ID, (UInt64)CreateFlags.Default);

				// Initialize event handlers and other functionalities here
				discord.SetLogHook(LogLevel.Debug, (level, message) =>
				{
					Log($"Discord[{level}]", message);
				});

				var activityManager = discord.GetActivityManager();
				activityManager.RegisterCommand($"{Assembly.GetEntryAssembly().Location} -o");

				activityManager.OnActivityJoin += (secret) =>
				{
					Log("INFO", $"Join Secret received: {secret}");
					string[] splitSecret = secret.Split('+');
					string url = $"https://novarin.co/discord-redirect-place?id={splitSecret[0]}&autoJoinJob={splitSecret[1]}";
					Process.Start(url);
					Environment.Exit(0);
					return;
				};

				while (true)
				{
					// Timeout after 1 minute
					if (DateTime.UtcNow.Subtract(startTime).TotalSeconds > 60)
					{
						Log("ERROR", "Timed out waiting for Discord callback, stopping the RPC.");
						Environment.Exit(0);
						return;
					}
					discord.RunCallbacks();
				}
			}
			else if (launchArgs.ProccessID == 0)
			{
				Log("WARN", "No process ID provided, stopping the RPC.");
				return;
			}

			try
			{
				robloxGameProccess = Process.GetProcessById(launchArgs.ProccessID);
				if (robloxGameProccess == null || robloxGameProccess.HasExited)
				{
					Log("ERROR", $"Failed to find Roblox process with ID: {launchArgs.ProccessID}");
					return;
				}
			}
			catch (Exception e)
			{
				Log("ERROR", $"Failed to find Roblox process with ID: {launchArgs.ProccessID} ({e.Message})");
				return;
			}

			// We check if Discord is running before we start the RPC
			Process discordProcess = GetDiscordProcess();
			if (discordProcess == null)
			{
				Log("INFO", "Discord is not running, waiting until it is...");
				while (discordProcess == null)
				{
					System.Threading.Thread.Sleep(1000);
					discordProcess = GetDiscordProcess();
					if (robloxGameProccess.HasExited)
					{
						Log("INFO", "Roblox process has exited, stopping the RPC.");
						Environment.Exit(0);
						return;
					}
				}
				// Discord has opened, we'll wait a bit for it to finish starting up
				System.Threading.Thread.Sleep(10000);
			}

			try
			{
				DoRPCOfPlace(robloxGameProccess, launchArgs);
			}
			catch (Exception e)
			{
				//Console.WriteLine("An error occurred: " + e.Message);
				//Console.WriteLine(e.StackTrace);
				//Console.ReadLine();
				throw e; // just for debugging lol
			}
		}

		public static void DoRPCOfPlace(Process robloxProcess, RPCLaunchArgs launchArgs)
		{
			var discord = new Discord.Discord(CLIENT_ID, (UInt64)CreateFlags.Default);

			// Initialize event handlers and other functionalities here
			discord.SetLogHook(LogLevel.Debug, (level, message) =>
			{
				Log($"Discord[{level}]", message);
			});

			PlaceInfo place = GetPlaceInfo(launchArgs.GameID);
			if (place == null)
			{
				Log("ERROR", "Failed to fetch place info from the API.");
				return;
			}
			if (place.Error != null)
			{
				Log("ERROR", $"Failed to fetch place info from the API: {place.Error}");
				return;
			}

			PlayersInJob playersInJob = GetPlayersInJob(launchArgs.JobID);
			if (playersInJob == null)
			{
				Log("ERROR", "Failed to fetch player count from the API.");
				return;
			}
			if (playersInJob.Error != null)
			{
				Log("ERROR", $"Failed to fetch player count from the API: {playersInJob.Error}");
				return;
			}

			var activityManager = discord.GetActivityManager();
			activityManager.RegisterCommand(Assembly.GetEntryAssembly().Location + " -o");

			long startPlayTime = (long)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;

			int loopsTillPlayerCountCheck = 50;
			int failureStrikes = 0;

			activityManager.OnActivityJoin += (secret) =>
			{
				Log("INFO", $"Join Secret received: {secret}");
				try
				{
					robloxProcess.CloseMainWindow();
				}
				catch (Exception) { }
				string[] splitSecret = secret.Split('+');
				string url = $"https://novarin.co/discord-redirect-place?id={splitSecret[0]}&autojoinJob={splitSecret[1]}";
				Process.Start(url);
				Environment.Exit(0);
				return;
			};


			// Main loop
			while (true)
			{
				
				if (failureStrikes >= 5)
				{
					Log("ERROR", "Failed to update activity 5 times in a row, stopping the RPC.");
					return;
				}

				if (loopsTillPlayerCountCheck >= 50)
				{
					loopsTillPlayerCountCheck = 0;
					playersInJob = GetPlayersInJob(launchArgs.JobID);
					if (playersInJob == null)
					{
						Log("ERROR", "Failed to fetch player count from the API.");
						failureStrikes++;
						break;
					}
					if (playersInJob.Error != null)
					{
						Log("ERROR", $"Failed to fetch player count from the API: {playersInJob.Error}");
						if (playersInJob.Error == "Job does not exist")
						{
							Log("ERROR", "Job not found, stopping the RPC.");
							return;
						}
						failureStrikes++;
						break;
					}

					activityManager.UpdateActivity(new Activity
					{
						State = "Playing",
						Details = $"Playing \"{place.Name}\"",
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
							Id = launchArgs.JobID,
							Size = {
								CurrentSize = playersInJob.PlayerCount,
								MaxSize = playersInJob.MaxPlayers,
							},
						},
						Secrets =
						{
							Join = $"{launchArgs.GameID}+{launchArgs.JobID}",
						},
						Instance = true,
					}, (res) =>
					{
						if (res == Result.Ok) Log("INFO", "Activity updated successfully!");
						else Log("ERROR", $"Failed to update activity: {res}");
					});

					// We succeed so lets reset the failure count
					failureStrikes = 0;
				}
				if (robloxProcess.HasExited)
				{
					Log("INFO", "Roblox process has exited, stopping the RPC.");
					return;
				}
				try
				{
					discord.RunCallbacks();
				} catch (Exception e)
				{
					Log("ERROR", $"Failed to run Discord callbacks: {e.Message}");
					failureStrikes++;
				}
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
					string receivedClientData = client.DownloadString("https://novarin.co/marketplace/productinfo?assetId=" + placeId);
					return JsonConvert.DeserializeObject<PlaceInfo>(receivedClientData);
				}
			}
			catch (Exception)
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
					string receivedClientData = client.DownloadString("https://novarin.co/app/api/games/playersInJob?jobid=" + jobId);
					return JsonConvert.DeserializeObject<PlayersInJob>(receivedClientData);
				}
			}
			catch (Exception)
			{
				return null;
			}
		}

		private static Process GetDiscordProcess()
		{
			Process[] processes = Process.GetProcesses();
			string[] targetProcessNames = { "Discord", "DiscordCanary", "DiscordPtb" };
			return Array.Find(processes, p => Array.Exists(targetProcessNames, name => p.ProcessName.Equals(name, StringComparison.OrdinalIgnoreCase)));
		}
	}

	class RPCLaunchArgs
	{
		[Option('g', "gameid", Required = false)]
		public string GameID { get; set; }
		[Option('j', "jobid", Required = false)]
		public string JobID { get; set; }
		[Option('l', "launchprotocol", Required = false)]
		public string LaunchProtocol { get; set; }
		[Option('p', "processid", Required = false)]
		public int ProccessID { get; set; }
		[Option('o', "discord-open", Required = false)]
		public bool OpenedByDiscord { get; set; }

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
