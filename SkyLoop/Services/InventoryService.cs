using FFXIVClientStructs.FFXIV.Client.Game;

namespace SkyLoop.Services;

public static unsafe class InventoryService
{
    private static readonly InventoryType[] Bags = [InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3, InventoryType.Inventory4];
    public static int FreeSlots()
    {
        if (Plugin.ObjectTable.LocalPlayer == null) return -1;
        var manager = InventoryManager.Instance();
        if (manager == null) return -1;
        var free = 0;
        foreach (var bag in Bags)
        {
            var container = manager->GetInventoryContainer(bag);
            if (container == null || !container->IsLoaded) return -1;
            for (var i = 0; i < container->Size; i++)
            {
                var slot = container->GetInventorySlot(i);
                if (slot != null && slot->ItemId == 0) free++;
            }
        }
        return free;
    }

    public static int Count(uint itemId)
    {
        if (itemId == 0 || Plugin.ObjectTable.LocalPlayer == null) return 0;
        var manager = InventoryManager.Instance();
        if (manager == null) return 0;
        var total = 0;
        foreach (var bag in Bags)
        {
            var container = manager->GetInventoryContainer(bag);
            if (container == null || !container->IsLoaded) return 0;
            for (var i = 0; i < container->Size; i++)
            {
                var slot = container->GetInventorySlot(i);
                // The virtual base-ID accessor normalizes NQ, HQ and collectable identities.
                // Count only carried bags: saddlebag/retainer changes cannot satisfy a macro.
                if (slot != null && slot->ItemId != 0 && slot->GetBaseItemId() == itemId)
                    total += slot->Quantity;
            }
        }
        return total;
    }
}
