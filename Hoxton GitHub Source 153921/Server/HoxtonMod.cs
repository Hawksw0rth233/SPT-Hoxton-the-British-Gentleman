using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Services.Modding.Custom;

namespace Hawks.Hoxton;

public record ModMetadata : IModMetadata
{
    public string ModGuid { get; init; } = "team.aminus.hoxton";
    public string Name { get; init; } = "Hoxton the British Gentleman";
    public string Author { get; init; } = "Hawks";
    public List<string>? Contributors { get; init; }
    public SemanticVersioning.Version Version { get; init; } = new("0.3.1");
    public SemanticVersioning.Range SptVersion { get; init; } = new("~4.1.6");
    public List<string>? Incompatibilities { get; init; }
    public Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; }
    public string? Url { get; init; }
    public string License { get; init; } = "Private local conversion";
    public bool HasPrepatcher { get; init; } = false;
}

[Injectable(TypePriority = OnLoadOrder.Preload + 1)]
public sealed class HoxtonMod(CustomItemService customItems, TemplateTable templates, TradersTable traders, LocaleTable locales) : IOnLoad
{
    const string Id = "686a04000000000000000001";
    const string Offer = "686a04000000000000000002";
    const string Donor = "657089638db3adca1009f4ca";
    const int Price = 55000;

    private static readonly (string Language, string Name, string Description)[] OriginalLocales =
    [
        ("en", "Hoxton Reborn", "The Hoxton Reborn mask is a mask that Hoxton made while in prison. For him, it's a symbol of everything he experienced in the past years while the Payday Gang kept on going without him. No one knows how his experiences in prison has changed him, and he won't talk about it when asked.$NL;$NL;All we can assume is that he has been changed forever, and that he is reborn into... Something different."),
        ("ch", "霍斯顿重生", "霍斯顿重生面具是霍斯顿在监狱中制作的。对他而言，这代表了过去几年，PAYDAY帮没有管他继续行动时，他独自承受的东西。没人知道他的牢狱生活改变了他什么，他也从来不会说起。$NL;$NL;我们只知道他被永远地改变了，重生为了......截然不同的他。"),
        ("jp", "Hoxton Reborn", "Hoxton Rebornマスクは、Hoxtonが服役中に作ったマスクである。彼にとってそれは、Paydayギャングが彼なしに活躍する一方で、彼がその間に経験したあらゆるものの象徴である。刑務所での彼の経験がどのようにして彼を変えたのか誰も知らず、尋ねられても彼は答えない。$NL;$NL;想像できるのは、彼が永遠に変わったこと、そして彼が... 何かに生まれ変わったということである。"),
    ];

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        var handbook = templates.Handbook.Items.Single(x=>x.Id==Donor);
        var result = customItems.CreateItemFromClone(new() {
            ItemTplToClone = Donor, NewId = Id, NewItemName = "hawks_hoxton_reborn",
            ParentId = templates.Items[Donor].Parent, HandbookParentId = handbook.ParentId,
            HandbookPriceRoubles = Price, FleaPriceRoubles = Price, Locales = new Dictionary<string, LocaleDetails>()
        });
        if (!result.Success) throw new InvalidOperationException(string.Join("; ", result.Errors));
        foreach (var (language, name, description) in OriginalLocales)
        {
            locales.Global[language].AddTransformer(data =>
            {
                data![Id + " Name"] = name;
                data[Id + " ShortName"] = name;
                data[Id + " Description"] = description;
                return data;
            });
        }
        var props = templates.Items[Id].Properties!;
        props.ArmorClass = 4;
        props.ArmorColliders = ["Eyes", "Jaw", "HeadCommon", "ParietalHead"];
        var assort = traders.GetTrader("54cb50c76803fa8b248b4571")!.Assort;
        assort.Items.Add(new Item { Id = Offer, Template = Id, ParentId = "hideout", SlotId = "hideout",
            Upd = new() { UnlimitedCount = true, StackObjectsCount = 999999 } });
        assort.BarterScheme[Offer] = [[new() { Template = "5449016a4bdc2d6f028b456f", Count = Price }]];
        assort.LoyalLevelItems[Offer] = 2;
        return Task.CompletedTask;
    }
}

[Injectable(TypePriority = OnLoadOrder.Preload + 1)]
public sealed class HoxtonHead(TemplateTable templates, LocaleTable locales) : IOnLoad
{
    private const string HeadId = "686a04000000000000000101";
    private const string NativeHeadId = "60a6aa8fd559ae040d0d951f";

    private static readonly (string Language, string Name)[] OriginalNames =
    [
        ("en", "Hoxton"),
        ("ch", "霍斯顿"),
        ("jp", "Hoxton"),
    ];

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        var donor = templates.Customization[NativeHeadId];
        templates.Customization.Add(HeadId, new()
        {
            Id = HeadId,
            Type = donor.Type,
            Parent = donor.Parent,
            Prototype = donor.Prototype,
            Name = "Hoxton",
            Properties = new()
            {
                Name = "Hoxton",
                ShortName = "Hoxton",
                Description = "Hoxton",
                Side = ["Usec", "Bear"],
                BodyPart = donor.Properties.BodyPart,
                ProfileVersions = [],
                AvailableAsDefault = true,
                Prefab = donor.Properties.Prefab
            }
        });
        foreach (var (language, name) in OriginalNames)
        {
            locales.Global[language].AddTransformer(data =>
            {
                data![HeadId + " Name"] = name;
                data[HeadId + " ShortName"] = name;
                data[HeadId + " Description"] = name;
                return data;
            });
        }
        templates.Character.Add(HeadId);
        templates.CustomisationStorage.Add(new()
        {
            Id = HeadId,
            Source = "default",
            Type = "head"
        });
        return Task.CompletedTask;
    }
}
