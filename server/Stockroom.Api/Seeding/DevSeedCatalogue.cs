using System.Globalization;

namespace Stockroom.Api.Seeding;

/// <summary>A product the dev seed creates. <paramref name="Barcodes"/> is how many EAN-13 codes it gets.</summary>
internal sealed record DevSeedProduct(string Sku, string Name, string? Description, int? MinStock, int Barcodes);

/// <summary>
/// The products of a small office and workshop storeroom, for development and screenshots (spec 14). SKUs
/// are hand-picked so they never clash with generated <c>SR-000123</c> ones.
/// </summary>
internal static class DevSeedCatalogue
{
    public static readonly IReadOnlyList<DevSeedProduct> Products =
    [
        new("OFF-PAP-A4", "Copy paper A4, 80 g, 500 sheets", "Ream of white multipurpose paper.", 10, 1),
        new("OFF-PAP-A3", "Copy paper A3, 80 g, 500 sheets", null, 2, 1),
        new("OFF-PEN-BLU", "Ballpoint pen, blue, box of 50", null, 2, 1),
        new("OFF-PEN-BLK", "Ballpoint pen, black, box of 50", null, 2, 1),
        new("OFF-MRK-WB4", "Whiteboard markers, 4 colours", "Dry-erase, bullet tip.", 3, 1),
        new("OFF-HLT-YEL", "Highlighter, yellow, pack of 10", null, 2, 1),
        new("OFF-NTB-A5", "Notebook A5, ruled, 96 pages", null, 10, 2),
        new("OFF-STK-7676", "Sticky notes 76 × 76 mm, 12 pads", null, 4, 1),
        new("OFF-STP-2610", "Staples 26/6, box of 5000", null, 3, 1),
        new("OFF-STP-DESK", "Desk stapler, 30 sheets", null, null, 1),
        new("OFF-ENV-DL", "Envelopes DL, self-seal, box of 500", null, 1, 1),
        new("OFF-ENV-C4", "Envelopes C4, box of 250", null, 1, 0),
        new("OFF-FLD-LVR", "Lever arch file, 75 mm, blue", null, 10, 1),
        new("OFF-PKT-A4", "Punched pockets A4, pack of 100", null, 3, 1),
        new("OFF-TAP-19", "Clear tape 19 mm × 33 m", null, 6, 1),
        new("OFF-LBL-L7160", "Address labels, 21 per sheet, 100 sheets", "Fits most laser printers.", 1, 1),
        new("PRN-TNR-K", "Toner cartridge, black", "For the second-floor laser printer.", 2, 1),
        new("PRN-TNR-C", "Toner cartridge, cyan", null, 1, 1),
        new("PRN-TNR-M", "Toner cartridge, magenta", null, 1, 1),
        new("PRN-TNR-Y", "Toner cartridge, yellow", null, 1, 1),
        new("IT-CBL-HDMI2", "HDMI cable, 2 m", null, 5, 1),
        new("IT-CBL-USBC1", "USB-C to USB-C cable, 1 m", null, 8, 2),
        new("IT-CBL-CAT6-3", "Patch cable Cat 6, 3 m, grey", null, 10, 1),
        new("IT-CBL-PWR-C13", "Power cable IEC C13, 1.8 m", null, 4, 0),
        new("IT-ADP-USBC-HDMI", "USB-C to HDMI adapter", null, 3, 1),
        new("IT-MSE-WL", "Wireless mouse", null, 3, 1),
        new("IT-KBD-USB-US", "USB keyboard, US layout", null, 2, 1),
        new("IT-HDS-USB", "USB headset with microphone", "For video calls.", 2, 1),
        new("IT-USB-64", "USB flash drive, 64 GB", null, 5, 1),
        new("IT-BAT-AA", "Batteries AA, pack of 24", null, 4, 1),
        new("IT-BAT-AAA", "Batteries AAA, pack of 24", null, 4, 1),
        new("IT-LAP-STND", "Laptop stand, aluminium", null, null, 1),
        new("CLN-WIP-DSF", "Disinfectant wipes, tub of 200", null, 6, 1),
        new("CLN-TWL-ROLL", "Paper towel roll, pack of 6", null, 8, 1),
        new("CLN-TIS-BOX", "Facial tissues, box", null, 12, 0),
        new("CLN-SOAP-5L", "Hand soap refill, 5 l", null, 2, 1),
        new("CLN-BAG-60", "Bin bags 60 l, roll of 50", null, 5, 1),
        new("CLN-GLV-M", "Nitrile gloves, medium, box of 100", null, 3, 1),
        new("CLN-GLV-L", "Nitrile gloves, large, box of 100", null, 3, 1),
        new("KIT-COF-BEAN", "Coffee beans, 1 kg", "Medium roast for the office machine.", 4, 1),
        new("KIT-TEA-ENG", "Black tea, 100 bags", null, 2, 1),
        new("KIT-SUG-STK", "Sugar sticks, box of 1000", null, 1, 1),
        new("KIT-MLK-UHT", "UHT milk, 1 l", null, 12, 1),
        new("KIT-CUP-PAP", "Paper cups 200 ml, sleeve of 50", null, 10, 0),
        new("KIT-DSH-TAB", "Dishwasher tablets, pack of 100", null, 1, 1),
        new("WRK-SCR-4X40", "Wood screws 4 × 40 mm, box of 200", null, 2, 1),
        new("WRK-TAP-DUCT", "Duct tape 50 mm × 25 m, silver", null, 3, 1),
        new("WRK-CBT-200", "Cable ties 200 mm, bag of 100", null, 2, 1),
        new("WRK-DRL-HSS", "HSS drill bit set, 19 pieces", null, null, 1),
        new("WRK-SFT-GLS", "Safety glasses, clear", null, 4, 1),
        new("WRK-LED-E27", "LED bulb E27, 9 W, warm white", null, 10, 2),
        new("WRK-EXT-4W", "Extension lead, 4 sockets, 3 m", "Surge protected.", 2, 1),
    ];

    /// <summary>
    /// The <paramref name="ordinal"/>th barcode of the product at <paramref name="index"/>: a valid EAN-13 with
    /// the GS1 Hungary prefix 599, unique within the catalogue.
    /// </summary>
    public static string Barcode(int index, int ordinal)
    {
        var digits = string.Create(CultureInfo.InvariantCulture, $"599{4_200_000 + (index * 10) + ordinal:D9}");
        return digits + CheckDigit(digits);
    }

    private static int CheckDigit(string twelveDigits)
    {
        // From the left, digits are weighted 1, 3, 1, …; the check digit rounds the sum up to a multiple of 10.
        var sum = 0;
        for (var i = 0; i < twelveDigits.Length; i++)
        {
            sum += (twelveDigits[i] - '0') * (i % 2 == 0 ? 1 : 3);
        }

        return (10 - (sum % 10)) % 10;
    }
}
