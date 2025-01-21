using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Runtime.Serialization;

using Streamer.bot.Plugin.Interface;

using Newtonsoft.Json;


public class CryptoRPG : CPHInlineBase
{
	#region String Constants

	// File Paths
	public const string KEY_LEVEL_DATA = "LevelUpDataPath";
	public const string KEY_MONSTER_DATA = "MonsterDataPath";
	public const string KEY_EQUIP_DATA = "EquipDataPath";

	// Attribute Names
	public const string KEY_TARGET_USER = "targetUser";
	public const string KEY_LAST_SEARCHED = "lastSearched";
	public const string KEY_USER = "user";
	public const string KEY_TIMER = "timer";
	public const string KEY_SHOP = "shop";
	public const string KEY_EXP_REWARD = "expReward";
	public const string KEY_GOLD_REWARD = "goldReward";
	public const string KEY_ESITO = "esito";
	public const string KEY_INPUT0 = "input0";
	public const string KEY_INPUT1 = "input1";
	public const string KEY_ATK_DELTA = "AttDelta";
	public const string KEY_DEF_DELTA = "DifDelta";
	public const string KEY_MAG_DELTA = "MagDelta";
	public const string KEY_SPR_DELTA = "SprDelta";

	public const string KEY_ATK = "patt";
	public const string KEY_DEF = "pdif";
	public const string KEY_MAG = "matt";
	public const string KEY_SPR = "mdif";
	public const string KEY_HP = "hp";

	// UserVariable Names
	public const string KEY_EXP = "exp";
	public const string KEY_LEVEL = "level";
	public const string KEY_AP = "AP";
	private const string KEY_GOLD = "gold";
	private const string KEY_EQUIP = "Equipment";

	// GlobalVariables Names
	public const string KEY_WEEKLY_SHOP = "weeklyShop";
	private const string SHOP_GEN_DATE_KEY = "ShopGenerationDate";

	// Stats Names
	public const string STAT_ATK = "ATK_FIS";
	public const string STAT_DEF = "DIF_FIS";
	public const string STAT_MAG = "ATK_MAG";
	public const string STAT_SPR = "DIF_MAG";
	public const string STAT_HP = "HP";

	// UserFacing Text
	private const string UF_MAG = "MAG";
	private const string UF_ATK = "ATT";
	private const string UF_SPR = "SPR";
	private const string UF_DEF = "DIF";

	#endregion String Constants

	#region Data

	private List<LevelBonus> m_Livelli;
	private List<Monster> m_Monsters = null;
	private List<Equip> m_Shop = null;
	private Dictionary<Equip.EquipType, List<Equip>> m_EquipDictionary;

	#endregion Data

	private Random m_rng;
	private int m_rounds = 0;

	public Random Rng
	{
		get
		{
			if (m_rng == null)
			{
				m_rng = new Random(DateTime.Now.Millisecond);
			}

			return m_rng;
		}
	}


	#region Shop

	public bool Shop()
	{
		string shopStr = CPH.GetGlobalVar<string>(KEY_WEEKLY_SHOP);

		List<Equip> shop = JsonConvert.DeserializeObject<List<Equip>>(shopStr);

		StringBuilder sb = new StringBuilder();

		sb.AppendLine("Shop Settimanale:");

		int index = 1;

		foreach (Equip equip in shop)
		{
			sb.AppendLine($"{index++}) {equip.Nome} - {equip.Price()}G - ATT:{equip.BonusATK} DIF:{equip.BonusDIF} MAG:{equip.BonusMAG} SPR:{equip.BonusSPR}");
		}

		CPH.SetArgument(KEY_SHOP, sb.ToString());

		return true;
	}

	public bool Compra()
	{
		string selectionStr = args[KEY_INPUT0].ToString();
		string shopStr = args[KEY_SHOP].ToString();
		string userName = args[KEY_USER].ToString();

		if (!Int32.TryParse(selectionStr, out int selection))
		{
			PrintShopInstructions();
			return false;
		}

		if (m_Shop == null)
		{
			m_Shop = JsonConvert.DeserializeObject<List<Equip>>(shopStr);
		}

		int price = m_Shop[selection - 1].Price();
		int playerGold = CPH.GetTwitchUserVar<int>(userName, KEY_GOLD);

		if (playerGold < price)
		{
			CPH.SendMessage($"Non hai abbastanza soldi per acquistare {m_Shop[selection - 1].Nome}. Hai {playerGold} monete ma te ne servono {price}");
			return false;
		}

		if (!TryAddItemToPlayer(userName, m_Shop[selection - 1]))
		{
			CPH.SendMessage($"Hai già un oggetto più potente di {m_Shop[selection - 1].Nome}");
			return false;
		}

		CPH.SendMessage($"{userName} ha acquistato {m_Shop[selection - 1].Nome} per {price} monete");

		m_Shop.RemoveAt(selection - 1);

		shopStr = JsonConvert.SerializeObject(m_Shop);

		CPH.SetGlobalVar(KEY_WEEKLY_SHOP, shopStr);
		CPH.SetTwitchUserVar(userName, KEY_GOLD, playerGold - price);

		return true;
	}

	private void PrintShopInstructions()
	{
		CPH.SendMessage("Per acquistare un oggetto usa il comando !compra X, " + "con X che indica il numero dell'oggetto da comprare all'interno dello shop");
	}

	private bool TryGetEquipDictionary(out Dictionary<Equip.EquipType, List<Equip>> dictionary)
	{
		dictionary = new Dictionary<Equip.EquipType, List<Equip>>();
		List<Equip> list;

		if (!TryGetEquipInfo(out list))
		{
			return false;
		}

		dictionary[Equip.EquipType.Elmo] = new List<Equip>();
		dictionary[Equip.EquipType.Arma] = new List<Equip>();
		dictionary[Equip.EquipType.Armatura] = new List<Equip>();
		dictionary[Equip.EquipType.Scudo] = new List<Equip>();
		dictionary[Equip.EquipType.Accessorio] = new List<Equip>();

		foreach (Equip item in list)
		{
			dictionary[item.Tipo].Add(item);
		}

		return true;
	}

	public bool GenerateShop()
	{
		try
		{
			TimeSpan cooldown = new TimeSpan(6, 23, 0, 0); // Settimanale

			var LastGenerated = CPH.GetGlobalVar<DateTime>(SHOP_GEN_DATE_KEY);

			TimeSpan fromLast = DateTime.Now - LastGenerated;

			if (fromLast < cooldown)
			{
				TimeSpan rem = cooldown - fromLast;

				CPH.SetArgument("Failed", $"Last created@ {LastGenerated}");
				return false;
			}

			CPH.SetGlobalVar(SHOP_GEN_DATE_KEY, DateTime.Now);

		}
		catch (Exception e)
		{
			CPH.SendMessage($"{e}");
			return false;
		}

		if (!TryGetEquipDictionary(out m_EquipDictionary))
		{
			return false;
		}

		List<Equip> shop = new List<Equip>();

		shop.Add(GetRandomEquip(m_EquipDictionary[Equip.EquipType.Elmo]));
		shop.Add(GetRandomEquip(m_EquipDictionary[Equip.EquipType.Arma]));
		shop.Add(GetRandomEquip(m_EquipDictionary[Equip.EquipType.Armatura]));
		shop.Add(GetRandomEquip(m_EquipDictionary[Equip.EquipType.Scudo]));
		shop.Add(GetRandomEquip(m_EquipDictionary[Equip.EquipType.Accessorio]));

		string shopStr = JsonConvert.SerializeObject(shop);

		CPH.SetGlobalVar(KEY_WEEKLY_SHOP, shopStr);

		return true;
	}

	private bool TryGetEquipInfo(out List<Equip> list)
	{
		string path = CPH.GetGlobalVar<string>(KEY_EQUIP_DATA, true);

		StreamReader streamReader = new StreamReader(path);

		list = new List<Equip>();

		while (!streamReader.EndOfStream)
		{
			string[] values = streamReader.ReadLine().Split(new char[] { ',' });

			if (values.Length != 7)
			{
				CPH.SendMessage("Error loading Equip data", true);
				return false;
			}

			Equip item = new Equip()
			{
				Nome = values[0],
				Tipo = (Equip.EquipType)Enum.Parse(typeof(Equip.EquipType), values[1]),
				BonusATK = Int32.Parse(values[2]),
				BonusDIF = Int32.Parse(values[3]),
				BonusMAG = Int32.Parse(values[4]),
				BonusSPR = Int32.Parse(values[5]),
				PowerLVL = Int32.Parse(values[6])
			};

			list.Add(item);
		}

		return true;
	}

	private Equip GetRandomEquip(List<Equip> equip)
	{

		return equip[Rng.Next(equip.Count)];
	}

	#endregion Shop

	#region Inventory

	private bool TryAddItemToPlayer(string userName, Equip equip)
	{
		string equipmentStr = CPH.GetTwitchUserVar<string>(userName, KEY_EQUIP);

		List<Equip> equipList = new List<Equip>();
		if (!string.IsNullOrEmpty(equipmentStr))
		{
			equipList = JsonConvert.DeserializeObject<List<Equip>>(equipmentStr);

			Equip preEquipped = equipList.Find(x => x.Tipo == equip.Tipo);
			if (preEquipped != null)
			{
				if (preEquipped.PowerLVL >= equip.PowerLVL)
				{
					CPH.SetArgument("Power levels?", $"{preEquipped.PowerLVL} - {equip.PowerLVL}");
					return false;
				}

				equipList.Remove(preEquipped);
			}
		}


		equipList.Add(equip);
		equipmentStr = JsonConvert.SerializeObject(equipList);
		CPH.SetTwitchUserVar(userName, KEY_EQUIP, equipmentStr);

		return true;
	}

	#endregion Inventory

	#region Character

	public bool AddExp()
	{
		TryGetUserName(out string userName);

		string incrementString;

		if (CPH.TryGetArg(KEY_INPUT1, out string incrementFromCommand))
		{
			incrementString = incrementFromCommand;
		}
		else if (CPH.TryGetArg(KEY_EXP_REWARD, out string incrementFromEncounter))
		{
			incrementString = incrementFromEncounter;
		}
		else
		{
			CPH.SendMessage("MANINNI?", true);
			return false;
		}

		var exp = CPH.GetTwitchUserVar<int>(userName, KEY_EXP, true);

		if (int.TryParse(incrementString, out int increment))
		{
			exp += increment;
		}
		else
		{
			CPH.SendMessage($"Valore incremento non valido", true);
			return false;
		}

		CPH.SetTwitchUserVar(userName, KEY_EXP, exp, true);

		return true;
	}

	public bool AddGold()
	{
		TryGetUserName(out string userName);

		string incrementString;

		if (CPH.TryGetArg("input1", out string incrementFromCommand))
		{
			incrementString = incrementFromCommand;
		}
		else if (CPH.TryGetArg("goldReward", out string incrementFromEncounter))
		{
			incrementString = incrementFromEncounter;
		}
		else
		{
			return false;
		}

		var gold = CPH.GetTwitchUserVar<int>(userName, "gold", true);

		if (int.TryParse(incrementString, out int increment))
		{
			gold += increment;
		}
		else
		{
			CPH.SendMessage($"Valore incremento non valido", true);
			return false;
		}

		CPH.SetTwitchUserVar(userName, "gold", gold, true);

		return true;
	}

	public bool CheckForLevelUp()
	{
		TryGetUserName(out string userName);

		if (m_Livelli == null && !TryGetLevelUpInfo(out m_Livelli))
		{
			CPH.SetArgument(KEY_ESITO, "Fallito");
			return false;
		}

		string incrementString = args[KEY_INPUT1].ToString();

		var exp = CPH.GetTwitchUserVar<int>(userName, KEY_EXP, true);

		if (!int.TryParse(incrementString, out int increment))
		{
			return false;
		}

		var pre_increment = exp - increment;

		int i;
		int expGate;

		for (i = 0; i < m_Livelli.Count; i++)
		{
			expGate = m_Livelli[i].Exp;

			if (pre_increment >= expGate)
			{
				continue;
			}

			if (exp >= expGate)
			{
				CPH.SetTwitchUserVar(userName, KEY_LEVEL, i + 1, true);
				break;
			}
			return false;
		}

		CPH.SendMessage($"{userName} ha raggiunto il livello {++i}!", true);

		var ap = CPH.GetTwitchUserVar<int>(userName, KEY_AP, true);

		CPH.SetTwitchUserVar(userName, KEY_AP, ap + 1, true);

		CPH.SetArgument(KEY_ATK_DELTA, m_Livelli[i].BonusATK);
		CPH.SetArgument(KEY_MAG_DELTA, m_Livelli[i].BonusMAG);
		CPH.SetArgument(KEY_DEF_DELTA, m_Livelli[i].BonusDEF);
		CPH.SetArgument(KEY_SPR_DELTA, m_Livelli[i].BonusSPR);

		int currVal = 0;
		int newTotDef = 0;

		currVal = CPH.GetTwitchUserVar<int>(userName, STAT_ATK, true);
		CPH.SetTwitchUserVar(userName, STAT_ATK, currVal + m_Livelli[i].BonusATK, true);

		currVal = CPH.GetTwitchUserVar<int>(userName, STAT_MAG, true);
		CPH.SetTwitchUserVar(userName, STAT_MAG, currVal + m_Livelli[i].BonusMAG, true);

		newTotDef += currVal = CPH.GetTwitchUserVar<int>(userName, STAT_DEF, true);
		CPH.SetTwitchUserVar(userName, STAT_DEF, currVal + m_Livelli[i].BonusDEF, true);

		newTotDef += currVal = CPH.GetTwitchUserVar<int>(userName, STAT_SPR, true);
		CPH.SetTwitchUserVar(userName, STAT_SPR, currVal + m_Livelli[i].BonusSPR, true);

		currVal = CPH.GetTwitchUserVar<int>(userName, STAT_HP, true);
		CPH.SetTwitchUserVar(userName, STAT_HP, currVal + m_Livelli[i].BonusHP + newTotDef, true);

		return true;
	}

	public bool FixLevelZeroBug()
	{
		if (!CPH.TryGetArg(KEY_USER, out string userName))
		{
			CPH.SendMessage("MANINNI?", true);
		}

		var level = CPH.GetTwitchUserVar<int>(userName, KEY_LEVEL, true);

		if (level == 0)
		{
			CPH.SetTwitchUserVar(userName, KEY_LEVEL, 1, true);

			CPH.SetTwitchUserVar(userName, STAT_ATK, 1, true);
			CPH.SetTwitchUserVar(userName, STAT_MAG, 1, true);

			CPH.SetTwitchUserVar(userName, STAT_DEF, 1, true);
			CPH.SetTwitchUserVar(userName, STAT_SPR, 1, true);
		}

		return true;
	}

	public bool GetTotalStats()
	{
		if (!TryGetUserName(out string userName))
		{
			return false;
		}

		int m_att = CPH.GetTwitchUserVar<int>(userName, STAT_MAG, true);
		int f_att = CPH.GetTwitchUserVar<int>(userName, STAT_ATK, true);
		int m_dif = CPH.GetTwitchUserVar<int>(userName, STAT_SPR, true);
		int f_dif = CPH.GetTwitchUserVar<int>(userName, STAT_DEF, true);

		string equipStr = CPH.GetTwitchUserVar<string>(userName, "Equipment", true);

		if (equipStr != null)
		{
			List<Equip> equip = JsonConvert.DeserializeObject<List<Equip>>(equipStr);

			if (equip == null || equip.Count == 0)
			{
				return false;
			}

			foreach (Equip equipItem in equip)
			{
				m_dif += equipItem.BonusSPR;
				m_att += equipItem.BonusMAG;
				f_dif += equipItem.BonusDIF;
				f_att += equipItem.BonusATK;
			}
		}

		CPH.SetArgument(KEY_MAG, m_att);
		CPH.SetArgument(KEY_ATK, m_dif);
		CPH.SetArgument(KEY_SPR, f_dif);
		CPH.SetArgument(KEY_DEF, f_att);

		return true;
	}

	public bool LevelUp()
	{
		if (!CPH.TryGetArg(KEY_USER, out string userName))
		{
			CPH.SendMessage("MANINNI?", true);
		}

		if (!CPH.TryGetArg(KEY_INPUT0, out string stat))
		{
			SendLevelUpInstructions();
			return false;
		}

		string apStr = args[KEY_AP].ToString();

		int ap = Int32.Parse(apStr);

		if (ap <= 0)
		{
			CPH.SendMessage("Non hai abbastanza AP per questa azione");
			return false;
		}

		// Process level up
		string statKey = "";
		switch (stat)
		{
			case UF_MAG:
				statKey = STAT_MAG;
				break;

			case UF_ATK:
				statKey = STAT_ATK;
				break;

			case UF_SPR:
				statKey = STAT_SPR;
				break;

			case UF_DEF:
				statKey = STAT_DEF;
				break;
		}

		if (string.IsNullOrEmpty(statKey))
		{
			SendLevelUpInstructions();
			return false;
		}


		int currVal = CPH.GetTwitchUserVar<int>(userName, statKey, true);
		CPH.SetTwitchUserVar(userName, statKey, currVal + 1, true);

		return true;
	}

	private void SendLevelUpInstructions()
	{
		CPH.SendMessage($"Per spendere AP riprova il comando e aggiungi la stat che vuoi aumentare. Le stat disponibili sono: {UF_MAG}, {UF_ATK}, {UF_SPR}, {UF_DEF}", true);
	}

	public bool Profilo()
	{
		if (!CPH.TryGetArg(KEY_USER, out string userName))
		{
			CPH.SendMessage("MANINNI?", true);
		}

		string level = args[KEY_LEVEL].ToString();
		string gold = args[KEY_GOLD].ToString();
		string exp = args[KEY_EXP].ToString();
		string ap = args[KEY_AP].ToString();

		int.TryParse(level, out int levelInt);
		int.TryParse(ap, out int apInt);

		if (m_Livelli == null && !TryGetLevelUpInfo(out m_Livelli))
		{
			CPH.SetArgument(KEY_ESITO, "Fallito");
			return false;
		}

		var expGateA = m_Livelli[levelInt].Exp;

		string message = $"{userName} - Livello {level} - {exp}/{expGateA} exp per il prossimo livello - {gold} monete";

		CPH.SetArgument("message", message);

		string apSection = "";

		if (apInt > 0)
		{
			apSection = $"Hai a disposizione {apInt} AP da spendere!";
		}

		CPH.SendMessage($"{message}. {apSection}", true);

		return true;
	}

	public bool Stats()
	{
		string hp = args[KEY_HP].ToString();
		string m_att = args[KEY_MAG].ToString();
		string f_att = args[KEY_ATK].ToString();
		string m_dif = args[KEY_SPR].ToString();
		string f_dif = args[KEY_DEF].ToString();

		CPH.SendMessage($"HP: {hp} - MAG: {m_att} - ATK: {f_att} - SPR: {m_dif} - DIF: {f_dif}");
		return true;
	}

	#endregion Character

	#region Encounters

	public bool Encounter()
	{
		m_rounds = 0;

		if (!TryGetUserName(out string userName))
		{
			CPH.SetArgument("errore", "Nome non valido");
			return false;
		}

		if (m_Monsters == null && !TryCreateMonsterList(out m_Monsters))
		{
			CPH.SetArgument(KEY_ESITO, "Fallito");
			return false;
		}

		var monster = RandomMonster(m_Monsters);

		Monster monsterCopy = new Monster
		{
			Name = monster.Name,
			MinGold = monster.MinGold,
			MaxGold = monster.MaxGold,
			MinExp = monster.MinExp,
			MaxExp = monster.MaxExp,
			AtkFis = monster.AtkFis,
			DifFis = monster.DifFis,
			AtkMag = monster.AtkMag,
			DifMag = monster.DifMag,
			Hp = monster.Hp
		};

		try
		{
			if (FightMonster(monsterCopy, userName))
			{
				int goldReward = Rng.Next(monster.MinExp, monster.MaxExp + 1);
				int expReward = Rng.Next(monster.MinGold, monster.MaxGold + 1);

				// variabile per gli exp
				CPH.SetArgument(KEY_EXP_REWARD, expReward);
				CPH.SetArgument(KEY_GOLD_REWARD, goldReward);

				CPH.SendMessage($"GG! {userName} ha sconfitto {monster.Name} dopo {m_rounds} round! Torna a casa con {goldReward} monete in tasca e ha guadagnato {expReward} punti esperienza", true);
				return true;
			}
		}
		catch (Exception e)
		{
			CPH.SendMessage($"{e}", true);
			return false;
		}

		CPH.SendMessage($"F! {userName} le ha prese di santa ragione da {monster.Name}. Sarà per la prossima volta!", true);

		return false;
	}

	private bool FightMonster(Monster monster, string userName)
	{
		int aMag = Int32.Parse(args[KEY_MAG].ToString());
		int aFis = Int32.Parse(args[KEY_ATK].ToString());
		int dMag = Int32.Parse(args[KEY_SPR].ToString());
		int dFis = Int32.Parse(args[KEY_DEF].ToString());

		var hp = CPH.GetTwitchUserVar<int>(userName, STAT_HP, true);

		while (hp > 0 && monster.Hp > 0)
		{
			m_rounds++;

			// Giocatore attacca
			monster.DifFis -= aFis;
			if (monster.DifFis < 0)
			{
				monster.Hp += monster.DifFis;
				monster.DifFis = 0;
			}
			monster.DifMag -= aMag;
			if (monster.DifMag < 0)
			{
				monster.Hp += monster.DifMag;
				monster.DifMag = 0;
			}

			// Mostro attacca
			dFis -= monster.AtkFis;
			if (dFis < 0)
			{
				hp += dFis;
				dFis = 0;
			}
			dMag -= monster.AtkMag;
			if (dMag < 0)
			{
				hp += dMag;
				dMag = 0;
			}
		}

		return hp > 0;
	}

	private Monster RandomMonster(List<Monster> list)
	{
		double a = Rng.NextDouble();

		int selection = (int)((1 - Math.Sqrt(1 - Math.Pow(a, 3))) * list.Count);

		return list[selection];
	}

	public bool Rovista()
	{
		TryGetUserName(out string userName);

		Random rng = new Random(DateTime.Now.Millisecond);

		double a = rng.NextDouble();

		int increment = (int)((1 - Math.Sqrt(1 - Math.Pow(a, 3))) * 15) + 1;

		CPH.SetTwitchUserVar(userName, KEY_LAST_SEARCHED, DateTime.Now, true);

		CPH.SetArgument(KEY_GOLD_REWARD, increment);
		CPH.SetArgument(KEY_TARGET_USER, userName);

		CPH.SendMessage($"{userName} ha rovistato nell'immondizia e trovato {increment} monete", true);

		return true;
	}

	#endregion Encounters

	#region DataManagement
	private bool TryGetLevelUpInfo(out List<LevelBonus> list)
	{
		string path = CPH.GetGlobalVar<string>(KEY_LEVEL_DATA, true);

		StreamReader streamReader = new StreamReader(path);

		list = new List<LevelBonus>();

		while (!streamReader.EndOfStream)
		{
			string[] values = streamReader.ReadLine().Split(new char[] { ',' });

			if (values.Length != 7)
			{
				CPH.SendMessage("Error loading level up data", true);
				return false;
			}

			LevelBonus levelBonus = new LevelBonus()
			{
				Livello = Int32.Parse(values[0]),
				Exp = Int32.Parse(values[1]),
				BonusHP = Int32.Parse(values[2]),
				BonusMAG = Int32.Parse(values[3]),
				BonusATK = Int32.Parse(values[4]),
				BonusSPR = Int32.Parse(values[5]),
				BonusDEF = Int32.Parse(values[6])
			};

			list.Add(levelBonus);
		}

		return true;
	}

	private bool TryCreateMonsterList(out List<Monster> list)
	{
		string path = CPH.GetGlobalVar<string>(KEY_MONSTER_DATA, true);

		CPH.SetArgument("Path", path);
		list = null;
		try
		{
			StreamReader streamReader = new StreamReader(path);

			list = new List<Monster>();

			while (!streamReader.EndOfStream)
			{
				string[] values = streamReader.ReadLine().Split(new char[] { ',' });

				if (values.Length != 11)
				{
					CPH.SendMessage("Error loading the encounter data", true);
					return false;
				}

				Monster monster = new Monster()
				{
					Name = values[0],
					MinGold = Int32.Parse(values[1]),
					MaxGold = Int32.Parse(values[2]),
					MinExp = Int32.Parse(values[3]),
					MaxExp = Int32.Parse(values[4]),
					AtkFis = Int32.Parse(values[5]),
					DifFis = Int32.Parse(values[6]),
					AtkMag = Int32.Parse(values[7]),
					DifMag = Int32.Parse(values[8]),
					Hp = Int32.Parse(values[9])
				};

				list.Add(monster);
			}
		}
		catch (Exception ex)
		{
			CPH.SetArgument("errore", ex.Message);
		}

		return true;
	}

	#endregion DataManagement

	#region Utilities

	private bool TryGetUserName(out string userName)
	{
		userName = "";

		if (CPH.TryGetArg(KEY_TARGET_USER, out string targetUser))
		{
			userName = targetUser;
		}
		else if (CPH.TryGetArg(KEY_USER, out string user))
		{
			userName = user;
		}

		return !string.IsNullOrEmpty(userName);
	}

	public bool Cooldown()
	{
		if (!CPH.TryGetArg(KEY_USER, out string userName))
		{
			CPH.SendMessage("MANINNI?", true);
			return false;
		}

		if (!CPH.TryGetArg(KEY_TIMER, out string timerName))
		{
			CPH.SendMessage("MANINNI?", true);
			return false;
		}

		try
		{
			TimeSpan cooldown = new TimeSpan(0, 5, 0);

			var lastEncounter = CPH.GetTwitchUserVar<DateTime>(userName, timerName, true);

			TimeSpan fromLast = DateTime.Now - lastEncounter;

			if (fromLast < cooldown)
			{
				TimeSpan rem = cooldown - fromLast;

				CPH.SendMessage($"Troppo presto! Riprova tra {rem.Minutes}m {rem.Seconds}s", true);
				return false;
			}

			CPH.SetTwitchUserVar(userName, timerName, DateTime.Now, true);

		}
		catch (Exception e)
		{
			CPH.SendMessage($"{e}");
			return false;
		}

		return true;
	}

	#endregion Utilities
}

public class LevelBonus
{
	public int Livello;
	public int Exp;
	public int BonusHP;
	public int BonusATK;
	public int BonusDEF;
	public int BonusMAG;
	public int BonusSPR;

}

[DataContract]
public class Equip
{
	public enum EquipType
	{
		Elmo,
		Arma,
		Scudo,
		Armatura,
		Accessorio
	}

	[DataMember] public string Nome;
	[DataMember] public EquipType Tipo;
	[DataMember] public int BonusATK;
	[DataMember] public int BonusMAG;
	[DataMember] public int BonusDIF;
	[DataMember] public int BonusSPR;
	[DataMember] public int PowerLVL;

	public int Price()
	{
		return PowerLVL * 10;
	}
}


public class Monster
{
	public string Name;
	public int MinGold;
	public int MaxGold;
	public int MinExp;
	public int MaxExp;
	public int AtkFis;
	public int DifFis;
	public int AtkMag;
	public int DifMag;
	public int Hp;
	public int Cr;
}