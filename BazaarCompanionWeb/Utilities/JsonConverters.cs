using System.Text.Json;
using System.Text.Json.Serialization;
using BazaarCompanionWeb.Models.Api.Items;

namespace BazaarCompanionWeb.Utilities;

public class TierConverter : JsonConverter<ItemTier>
{
    public override ItemTier Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var tier = reader.GetString();
        return tier switch
        {
            "COMMON" => ItemTier.Common,
            "UNCOMMON" => ItemTier.Uncommon,
            "RARE" => ItemTier.Rare,
            "EPIC" => ItemTier.Epic,
            "LEGENDARY" => ItemTier.Legendary,
            "MYTHIC" => ItemTier.Mythic,
            "SUPREME" => ItemTier.Supreme,
            "SPECIAL" => ItemTier.Special,
            "VERY_SPECIAL" => ItemTier.VerySpecial,
            "UNOBTAINABLE" => ItemTier.Unobtainable,
            _ => throw new JsonException($"Failed to convert string to enum: {tier}")
        };
    }

    public override void Write(Utf8JsonWriter writer, ItemTier value, JsonSerializerOptions options)
    {
        throw new NotImplementedException();
    }
}
