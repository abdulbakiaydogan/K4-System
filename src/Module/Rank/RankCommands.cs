namespace K4System
{
	using MySqlConnector;

	using CounterStrikeSharp.API;
	using CounterStrikeSharp.API.Core;
	using CounterStrikeSharp.API.Modules.Admin;
	using CounterStrikeSharp.API.Modules.Commands;
	using CounterStrikeSharp.API.Modules.Commands.Targeting;
	using CounterStrikeSharp.API.Modules.Menu;
	using Microsoft.Extensions.Logging;
	using System.Data;

	public partial class ModuleRank : IModuleRank
	{
		public void Initialize_Commands(Plugin plugin)
		{
			CommandSettings commands = Config.CommandSettings;

			commands.RankCommands.ForEach(commandString =>
			{
				plugin.AddCommand($"css_{commandString}", "Check the current rank and points", plugin.CallbackAnonymizer(OnCommandRank));
			});

			commands.RanksCommands.ForEach(commandString =>
			{
				plugin.AddCommand($"css_{commandString}", "Check the available ranks and their data", plugin.CallbackAnonymizer(OnCommandRanks));
			});

			commands.ResetMyCommands.ForEach(commandString =>
			{
				plugin.AddCommand($"css_{commandString}", "Resets the player's own points to zero", plugin.CallbackAnonymizer(OnCommandResetMyRank));
			});

			commands.TopCommands.ForEach(commandString =>
			{
				plugin.AddCommand($"css_{commandString}", "Check the top players by points", plugin.CallbackAnonymizer(OnCommandTop));
			});

			plugin.AddCommand("css_resetrank", "Resets the targeted player's points to zero", plugin.CallbackAnonymizer(OnCommandResetRank));
			plugin.AddCommand("css_setpoints", "SEt the targeted player's points", plugin.CallbackAnonymizer(OnCommandSetPoints));
			plugin.AddCommand("css_givepoints", "Give points the targeted player", plugin.CallbackAnonymizer(OnCommandGivePoints));
			plugin.AddCommand("css_removepoints", "Remove points from the targeted player", plugin.CallbackAnonymizer(OnCommandRemovePoints));
		}

		public void OnCommandRank(CCSPlayerController? player, CommandInfo info)
		{
			if (player == null || !player.IsValid || player.PlayerPawn.Value == null)
				return;

			Plugin plugin = (this.PluginContext.Plugin as Plugin)!;

			if (!PlayerCache.Instance.ContainsPlayer(player))
			{
				info.ReplyToCommand($" {plugin.Localizer["k4.general.prefix"]} {plugin.Localizer["k4.general.loading"]}");
				return;
			}

			int printCount = 5;

			if (int.TryParse(info.ArgByIndex(1), out int parsedInt))
			{
				printCount = Math.Clamp(parsedInt, 1, 25);
			}

			string steamID = player!.SteamID.ToString();

			Task<(int playerPlace, int totalPlayers)> task = Task.Run(() => FetchRankDataAsync(steamID));
			task.Wait();
			var result = task.Result;

			int playerPlace = result.playerPlace;
			int totalPlayers = result.totalPlayers;

			RankData? playerData = PlayerCache.Instance.GetPlayerData(player).rankData;

			if (playerData is null)
				return;

			int higherRanksCount = rankDictionary.Count(kv => kv.Value.Point > playerData.Points);

			info.ReplyToCommand($" {plugin.Localizer["k4.general.prefix"]} {plugin.Localizer["k4.ranks.rank.title", player.PlayerName]}");
			info.ReplyToCommand(plugin.Localizer["k4.ranks.rank.line1", playerData.Points, playerData.Rank.Color, playerData.Rank.Name, rankDictionary.Count - higherRanksCount, rankDictionary.Count]);

			KeyValuePair<string, Rank> nextRankEntry = rankDictionary
						.Where(kv => kv.Value.Point > playerData.Rank.Point)
						.OrderBy(kv => kv.Value.Point)
						.FirstOrDefault();

			if (nextRankEntry.Value != null)
			{
				Rank nextRank = nextRankEntry.Value;

				info.ReplyToCommand(plugin.Localizer["k4.ranks.rank.line2", nextRank.Color, nextRank.Name]);
				info.ReplyToCommand(plugin.Localizer["k4.ranks.rank.line3", nextRank.Point - playerData.Points]);
			}

			info.ReplyToCommand(plugin.Localizer["k4.ranks.rank.line4", playerPlace, totalPlayers]);
		}

		public async Task<(int, int)> FetchRankDataAsync(string steamID)
		{
			try
			{
				var (playerPlace, totalPlayers) = await GetPlayerPlaceAndCountAsync(steamID);
				return (playerPlace, totalPlayers);
			}
			catch (Exception ex)
			{
				Logger.LogError($"Error fetching rank data: {ex.Message}");
				return (0, 0);
			}
		}

		public void OnCommandRanks(CCSPlayerController? player, CommandInfo info)
		{
			Plugin plugin = (this.PluginContext.Plugin as Plugin)!;

			if (!plugin.CommandHelper(player, info, CommandUsage.CLIENT_ONLY))
				return;

			MenuManager.OpenChatMenu(player!, ranksMenu);
		}

		public void OnCommandResetMyRank(CCSPlayerController? player, CommandInfo info)
		{
			Plugin plugin = (this.PluginContext.Plugin as Plugin)!;

			if (!plugin.CommandHelper(player, info, CommandUsage.CLIENT_ONLY))
				return;

			if (!PlayerCache.Instance.ContainsPlayer(player!))
			{
				info.ReplyToCommand($" {plugin.Localizer["k4.general.prefix"]} {plugin.Localizer["k4.general.loading"]}");
				return;
			}

			RankData? playerData = PlayerCache.Instance.GetPlayerData(player!).rankData;

			if (playerData is null)
				return;

			playerData.RoundPoints -= playerData.Points;
			playerData.Points = 0;

			plugin.SavePlayerCache(player!, false);

			Server.PrintToChatAll($" {plugin.Localizer["k4.general.prefix"]} {plugin.Localizer["k4.ranks.resetmyrank", player!.PlayerName]}");
		}

		public void OnCommandTop(CCSPlayerController? player, CommandInfo info)
		{
			if (player == null || !player.IsValid || player.PlayerPawn.Value == null)
				return;

			Plugin plugin = (this.PluginContext.Plugin as Plugin)!;

			if (!PlayerCache.Instance.ContainsPlayer(player))
			{
				info.ReplyToCommand($" {plugin.Localizer["k4.general.prefix"]} {plugin.Localizer["k4.general.loading"]}");
				return;
			}

			int printCount = 5;

			if (int.TryParse(info.ArgByIndex(1), out int parsedInt))
			{
				printCount = Math.Clamp(parsedInt, 1, 25);
			}

			CCSPlayerController savedPlayer = player;
			List<PlayerData> playersData = plugin.PreparePlayersData();

			Task<List<(int points, string name)>?> task = Task.Run(() => FetchTopDataAsync(printCount));
			task.Wait();
			List<(int points, string name)>? rankData = task.Result;

			if (rankData?.Count > 0)
			{
				for (int i = 0; i < rankData.Count; i++)
				{
					int points = rankData[i].points;
					string name = rankData[i].name;

					Rank rank = GetPlayerRank(points);

					player.PrintToChat($" {plugin.Localizer["k4.ranks.top.line", i + 1, rank.Color, rank.Name, name, points]}");
				}
			}
			else
			{
				player.PrintToChat($" {plugin.Localizer["k4.general.prefix"]} {plugin.Localizer["k4.ranks.top.notfound", printCount]}");
			}
		}

		public async Task<List<(int points, string name)>?> FetchTopDataAsync(int printCount)
		{
			string query = $"SELECT `points`, `name` FROM `{Config.DatabaseSettings.TablePrefix}k4ranks` ORDER BY `points` DESC LIMIT {printCount};";

			List<(int points, string name)> rankData = new List<(int points, string name)>();

			try
			{

				using (MySqlCommand command = new MySqlCommand(query))
				{
					DataTable dataTable = await Database.Instance.ExecuteReaderAsync(command.CommandText);

					if (dataTable.Rows.Count > 0)
					{
						foreach (DataRow row in dataTable.Rows)
						{
							int points = Convert.ToInt32(row[0]);
							string name = Convert.ToString(row[1]) ?? "Unknown";
							rankData.Add((points, name));
						}
					}
				}

				return rankData;
			}
			catch (Exception ex)
			{
				Logger.LogError($"A problem occurred while fetching top data: {ex.Message}");
				return null;
			}
		}

		public void OnCommandResetRank(CCSPlayerController? player, CommandInfo info)
		{
			Plugin plugin = (this.PluginContext.Plugin as Plugin)!;

			if (!plugin.CommandHelper(player, info, CommandUsage.CLIENT_AND_SERVER, 1, "<target>", "@k4system/admin"))
				return;

			string playerName = player != null && player.IsValid && player.PlayerPawn.Value != null ? player.PlayerName : "SERVER";

			TargetResult targetResult = info.GetArgTargetResult(1);

			if (!targetResult.Any())
			{
				info.ReplyToCommand($" {plugin.Localizer["k4.general.prefix"]} {plugin.Localizer["k4.general.targetnotfound"]}");
				return;
			}

			foreach (CCSPlayerController target in targetResult.Players)
			{
				if (target.IsBot || target.IsHLTV)
				{
					info.ReplyToCommand($" {plugin.Localizer["k4.general.prefix"]} {plugin.Localizer["k4.general.targetnobot", target.PlayerName]}");
					continue;
				}

				if (!AdminManager.CanPlayerTarget(player, target))
				{
					info.ReplyToCommand($" {plugin.Localizer["k4.general.prefix"]} {plugin.Localizer["k4.general.targetimmunity", target.PlayerName]}");
					continue;
				}

				if (!PlayerCache.Instance.ContainsPlayer(target))
				{
					info.ReplyToCommand($" {plugin.Localizer["k4.general.prefix"]} {plugin.Localizer["k4.general.targetloading", target.PlayerName]}");
					continue;
				}

				RankData? playerData = PlayerCache.Instance.GetPlayerData(target).rankData;

				if (playerData is null)
					return;

				playerData.RoundPoints -= playerData.Points;
				playerData.Points = 0;

				plugin.SavePlayerCache(target, false);

				if (playerName != "SERVER")
					Server.PrintToChatAll($" {plugin.Localizer["k4.general.prefix"]} {plugin.Localizer["k4.ranks.resetrank", target.PlayerName, playerName]}");
			}
		}

		public void OnCommandSetPoints(CCSPlayerController? player, CommandInfo info)
		{
			Plugin plugin = (this.PluginContext.Plugin as Plugin)!;

			if (!plugin.CommandHelper(player, info, CommandUsage.CLIENT_AND_SERVER, 2, "<target> <amount>", "@k4system/admin"))
				return;

			string playerName = player != null && player.IsValid && player.PlayerPawn.Value != null ? player.PlayerName : "SERVER";

			if (!int.TryParse(info.ArgByIndex(2), out int parsedInt))
			{
				info.ReplyToCommand($" {plugin.Localizer["k4.general.prefix"]} {plugin.Localizer["k4.general.invalidamount"]}");
				return;
			}

			TargetResult targetResult = info.GetArgTargetResult(1);

			if (!targetResult.Any())
			{
				info.ReplyToCommand($" {plugin.Localizer["k4.general.prefix"]} {plugin.Localizer["k4.general.targetnotfound"]}");
				return;
			}

			foreach (CCSPlayerController target in targetResult.Players)
			{
				if (target.IsBot || target.IsHLTV)
				{
					info.ReplyToCommand($" {plugin.Localizer["k4.general.prefix"]} {plugin.Localizer["k4.general.targetnobot", target.PlayerName]}");
					continue;
				}

				if (!AdminManager.CanPlayerTarget(player, target))
				{
					info.ReplyToCommand($" {plugin.Localizer["k4.general.prefix"]} {plugin.Localizer["k4.general.targetimmunity", target.PlayerName]}");
					continue;
				}

				if (!PlayerCache.Instance.ContainsPlayer(target))
				{
					info.ReplyToCommand($" {plugin.Localizer["k4.general.prefix"]} {plugin.Localizer["k4.general.targetloading", target.PlayerName]}");
					continue;
				}

				RankData? playerData = PlayerCache.Instance.GetPlayerData(target).rankData;

				if (playerData is null)
					return;

				playerData.RoundPoints = parsedInt;
				playerData.Points = 0;

				plugin.SavePlayerCache(target, false);

				if (playerName != "SERVER")
					Server.PrintToChatAll($" {plugin.Localizer["k4.general.prefix"]} {plugin.Localizer["k4.ranks.setpoints", target.PlayerName, parsedInt, playerName]}");
			}
		}

		public void OnCommandGivePoints(CCSPlayerController? player, CommandInfo info)
		{
			Plugin plugin = (this.PluginContext.Plugin as Plugin)!;

			if (!plugin.CommandHelper(player, info, CommandUsage.CLIENT_AND_SERVER, 2, "<target> <amount>", "@k4system/admin"))
				return;

			string playerName = player != null && player.IsValid && player.PlayerPawn.Value != null ? player.PlayerName : "SERVER";

			if (!int.TryParse(info.ArgByIndex(2), out int parsedInt))
			{
				info.ReplyToCommand($" {plugin.Localizer["k4.general.prefix"]} {plugin.Localizer["k4.general.invalidamount"]}");
				return;
			}

			TargetResult targetResult = info.GetArgTargetResult(1);

			if (!targetResult.Any())
			{
				info.ReplyToCommand($" {plugin.Localizer["k4.general.prefix"]} {plugin.Localizer["k4.general.targetnotfound"]}");
				return;
			}

			foreach (CCSPlayerController target in targetResult.Players)
			{
				if (target.IsBot || target.IsHLTV)
				{
					info.ReplyToCommand($" {plugin.Localizer["k4.general.prefix"]} {plugin.Localizer["k4.general.targetnobot", target.PlayerName]}");
					continue;
				}

				if (!AdminManager.CanPlayerTarget(player, target))
				{
					info.ReplyToCommand($" {plugin.Localizer["k4.general.prefix"]} {plugin.Localizer["k4.general.targetimmunity", target.PlayerName]}");
					continue;
				}

				if (!PlayerCache.Instance.ContainsPlayer(target))
				{
					info.ReplyToCommand($" {plugin.Localizer["k4.general.prefix"]} {plugin.Localizer["k4.general.targetloading", target.PlayerName]}");
					continue;
				}

				RankData? playerData = PlayerCache.Instance.GetPlayerData(target).rankData;

				if (playerData is null)
					return;

				playerData.RoundPoints += parsedInt;
				playerData.Points += parsedInt;

				plugin.SavePlayerCache(target, false);

				if (playerName != "SERVER")
					Server.PrintToChatAll($" {plugin.Localizer["k4.general.prefix"]} {plugin.Localizer["k4.ranks.givepoints", playerName, parsedInt, target.PlayerName]}");
			}
		}

		public void OnCommandRemovePoints(CCSPlayerController? player, CommandInfo info)
		{
			Plugin plugin = (this.PluginContext.Plugin as Plugin)!;

			if (!plugin.CommandHelper(player, info, CommandUsage.CLIENT_AND_SERVER, 2, "<target> <amount>", "@k4system/admin"))
				return;

			string playerName = player != null && player.IsValid && player.PlayerPawn.Value != null ? player.PlayerName : "SERVER";

			if (!int.TryParse(info.ArgByIndex(2), out int parsedInt))
			{
				info.ReplyToCommand($" {plugin.Localizer["k4.general.prefix"]} {plugin.Localizer["k4.general.invalidamount"]}");
				return;
			}

			TargetResult targetResult = info.GetArgTargetResult(1);

			if (!targetResult.Any())
			{
				info.ReplyToCommand($" {plugin.Localizer["k4.general.prefix"]} {plugin.Localizer["k4.general.targetnotfound"]}");
				return;
			}

			foreach (CCSPlayerController target in targetResult.Players)
			{
				if (target.IsBot || target.IsHLTV)
				{
					info.ReplyToCommand($" {plugin.Localizer["k4.general.prefix"]} {plugin.Localizer["k4.general.targetnobot", target.PlayerName]}");
					continue;
				}

				if (!AdminManager.CanPlayerTarget(player, target))
				{
					info.ReplyToCommand($" {plugin.Localizer["k4.general.prefix"]} {plugin.Localizer["k4.general.targetimmunity", target.PlayerName]}");
					continue;
				}

				if (!PlayerCache.Instance.ContainsPlayer(target))
				{
					info.ReplyToCommand($" {plugin.Localizer["k4.general.prefix"]} {plugin.Localizer["k4.general.targetloading", target.PlayerName]}");
					continue;
				}

				RankData? playerData = PlayerCache.Instance.GetPlayerData(target).rankData;

				if (playerData is null)
					return;

				playerData.RoundPoints -= parsedInt;
				playerData.Points -= parsedInt;

				plugin.SavePlayerCache(target, false);

				if (playerName != "SERVER")
					Server.PrintToChatAll($" {plugin.Localizer["k4.general.prefix"]} {plugin.Localizer["k4.ranks.removepoints", playerName, parsedInt, target.PlayerName]}");
			}
		}
	}
}
