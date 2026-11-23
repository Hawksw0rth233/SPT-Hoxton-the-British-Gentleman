using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace Hawks.Hoxton;

[Injectable(TypePriority = OnLoadOrder.Preload + 1)]
public sealed class HoxtonVoice(TemplateTable templates, LocaleTable locales) : IOnLoad
{
    private const string VoiceId = "686a04000000000000000102";
    private const string DonorVoice = "5fc1223595572123ae7384a3";

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        var donor = templates.Customization[DonorVoice];
        templates.Customization.Add(VoiceId, new()
        {
            Id = VoiceId,
            Name = "Hoxton",
            Type = donor.Type,
            Parent = donor.Parent,
            Prototype = donor.Prototype,
            Properties = new()
            {
                Name = "Hoxton",
                ShortName = "Hoxton",
                Description = "Hoxton",
                Side = ["Usec", "Bear"],
                ProfileVersions = [],
                AvailableAsDefault = true,
                IsNotRandom = true,
                Prefab = "Hoxton",
            }
        });
        templates.Character.Add(VoiceId);
        templates.CustomisationStorage.Add(new() { Id = VoiceId, Type = "voice", Source = "default" });
        foreach (var (language, name) in new[] { ("en", "Hoxton"), ("ch", "霍斯顿"), ("jp", "Hoxton") })
        {
            locales.Global[language].AddTransformer(data =>
            {
                data![VoiceId + " Name"] = name;
                data[VoiceId + " ShortName"] = name;
                data[VoiceId + " Description"] = name;
                return data;
            });
        }
        return Task.CompletedTask;
    }
}
