using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace Hawks.Hoxton;

[Injectable(TypePriority = OnLoadOrder.Preload + 1)]
public sealed class HoxtonClothing(TemplateTable templates, TradersTable traders, LocaleTable locales) : IOnLoad
{
    private const string Upper = "686a04000000000000000201";
    private const string Hands = "686a04000000000000000202";
    private const string Lower = "686a04000000000000000203";
    private const string UpperSuite = "686a04000000000000000204";
    private const string LowerSuite = "686a04000000000000000205";
    private const string Ragman = "5ac3b934156ae10c4430e83c";

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        AddPart(Upper, "5cde95d97d6c8b647a3769b0", "Hoxton jacket");
        AddPart(Hands, "5cde95fa7d6c8b04737c2d13", "Hoxton gloves");
        AddPart(Lower, "5cde95ef7d6c8b04713c4f2d", "Hoxton trousers");
        AddSuite(UpperSuite, "5cde9ec17d6c8b04723cf479", "Hoxton jacket",
            "686a04000000000000000206", upper: true);
        AddSuite(LowerSuite, "5cde9e957d6c8b0474535da7", "Hoxton trousers",
            "686a04000000000000000207", upper: false);
        AddLocales();
        return Task.CompletedTask;
    }

    private void AddPart(string id, string donorId, string name)
    {
        var donor = templates.Customization[donorId];
        templates.Customization.Add(id, new()
        {
            Id = id,
            Type = donor.Type,
            Parent = donor.Parent,
            Prototype = donor.Prototype,
            Name = name,
            Properties = new()
            {
                Name = name, ShortName = name, Description = name,
                Side = ["Usec", "Bear"], ProfileVersions = [],
                AvailableAsDefault = false, BodyPart = donor.Properties.BodyPart,
                Prefab = donor.Properties.Prefab
            }
        });
        templates.Character.Add(id);
    }

    private void AddSuite(string id, string donorId, string name, string offer, bool upper)
    {
        var donor = templates.Customization[donorId];
        templates.Customization.Add(id, new()
        {
            Id = id,
            Type = donor.Type,
            Parent = donor.Parent,
            Prototype = donor.Prototype,
            Name = name,
            Properties = new()
            {
                Name = name, ShortName = name, Description = name,
                Side = ["Usec", "Bear"], ProfileVersions = [], Game = ["regular"],
                AvailableAsDefault = false
            }
        });
        var props = templates.Customization[id].Properties;
        if (upper)
        {
            props.Body = Upper;
            props.Hands = Hands;
        }
        else
        {
            props.Feet = Lower;
        }
        templates.Character.Add(id);
        traders.GetTrader(Ragman)!.Suits!.Add(new Suit
        {
            Id = offer, Tid = Ragman, SuiteId = id,
            IsActive = true, ExternalObtain = false, InternalObtain = true, IsHiddenInPVE = false,
            Requirements = new()
            {
                RequiredTid = Ragman,
                LoyaltyLevel = 0, ProfileLevel = 0, Standing = 0, PrestigeLevel = 0,
                ItemRequirements = [], QuestRequirements = [], SkillRequirements = [], AchievementRequirements = []
            }
        });
    }

    private void AddLocales()
    {
        foreach (var (language, jacket, trousers, gloves) in new[]
        {
            ("en", "Hoxton's suit jacket", "Hoxton's suit trousers", "Hoxton's blue latex gloves"),
            ("ch", "Hoxton 的西装上衣", "Hoxton 的西裤", "Hoxton 的蓝色乳胶手套"),
            ("jp", "Hoxtonのスーツジャケット", "Hoxtonのスーツパンツ", "Hoxtonの青いラテックス手袋")
        })
        {
            locales.Global[language].AddTransformer(data =>
            {
                foreach (var (id, name) in new[] { (Upper, jacket), (UpperSuite, jacket),
                    (Lower, trousers), (LowerSuite, trousers), (Hands, gloves) })
                {
                    data![id + " Name"] = name;
                    data[id + " ShortName"] = name;
                    data[id + " Description"] = name;
                }
                return data;
            });
        }
    }
}
