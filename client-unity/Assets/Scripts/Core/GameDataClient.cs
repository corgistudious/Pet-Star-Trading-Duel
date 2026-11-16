using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PetStar.Rules;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace PetStar.Client.Core
{
    /// <summary>Display-only copy of design data (names, lore, skills) and art loading via Addressables.
    /// Gameplay numbers that matter (stats, results, balances) always come from the server responses.</summary>
    public static class GameDataClient
    {
        public static Dictionary<string, PetFormDef> Forms { get; } = new();
        public static Dictionary<string, PetFamilyDef> Families { get; } = new();
        public static Dictionary<string, SkillDef> Skills { get; } = new();
        public static Dictionary<string, Color> RarityColors { get; } = new();
        public static Dictionary<string, Color> ElementColors { get; } = new();
        public static JObject CardLayout { get; private set; } = new();

        private static readonly Dictionary<string, Sprite> SpriteCache = new();

        public static void Init()
        {
            var pets = JObject.Parse(Res("pets"));
            foreach (var fam in pets["families"]!.ToObject<List<PetFamilyDef>>(JsonSerializer.Create(ApiClient.Json))!)
            {
                Families[fam.Id] = fam;
                foreach (var f in fam.Forms)
                {
                    f.FamilyId = fam.Id; f.Element = fam.Element; f.Role = fam.Role; f.FamilyStages = fam.Stages;
                    Forms[f.Id] = f;
                }
            }
            foreach (var s in JObject.Parse(Res("skills"))["skills"]!.ToObject<List<SkillDef>>(JsonSerializer.Create(ApiClient.Json))!)
                Skills[s.Id] = s;
            foreach (var r in JObject.Parse(Res("rarities"))["rarities"]!)
                RarityColors[(string)r["id"]!] = Hex((string)r["color"]!);
            foreach (var e in JObject.Parse(Res("elements"))["elements"]!)
                ElementColors[(string)e["id"]!] = Hex((string)e["color"]!);
            CardLayout = JObject.Parse(Res("card-layout"));
        }

        private static string Res(string name) => Resources.Load<TextAsset>("Data/" + name)?.text ?? "{}";

        public static Color Hex(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;

        /// <summary>Localised name ("pet.{id}" for Divine Pets with vi/en names), else the species name.</summary>
        public static string PetName(string formId)
        {
            var k = "pet." + formId;
            var t = L.T(k);
            if (t != k) return t;
            return Forms.TryGetValue(formId, out var f) ? f.Name : formId;
        }

        public static string Lore(string formId) =>
            Forms.TryGetValue(formId, out var f) && f.Lore.TryGetValue(L.Language, out var t) ? t : (Forms.TryGetValue(formId, out f) && f.Lore.TryGetValue("en", out t) ? t : "");

        /// <summary>Addressable keys: "pet/{formId}" and "ui/{assetId}" (set by the editor import tool).</summary>
        public static async Task<Sprite?> LoadSprite(string address)
        {
            if (SpriteCache.TryGetValue(address, out var s)) return s;
            var handle = Addressables.LoadAssetAsync<Sprite>(address);
            while (!handle.IsDone) await Task.Yield();
            if (handle.Status != UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Succeeded)
            {
                Debug.LogWarning("Missing addressable sprite " + address);
                return null;
            }
            SpriteCache[address] = handle.Result;
            return handle.Result;
        }

        /// <summary>Pack art address: pack type "basic_pack" uses asset "ui_pack_basic" (packs.json artAssetId convention).</summary>
        public static string PackArt(string packTypeId) =>
            "ui/ui_pack_" + (packTypeId.EndsWith("_pack") ? packTypeId.Substring(0, packTypeId.Length - 5) : packTypeId);

        public static string CurrencyIcon(string currency) =>
            currency == Currency.RainbowStar ? "ui/ui_currency_rainbow_star" : "ui/ui_currency_mini_green_bean";

        public static IEnumerable<string> RarityOrder => new[] { "N", "R", "SR", "SSR", "UR", "LR", "MR", "GR", "EX", "Secret", "DV" };
        public static int RarityRank(string r) => RarityOrder.ToList().IndexOf(r);
    }
}
