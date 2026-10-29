using System;
using UnityEngine;

namespace BR.Inventory
{
    /// <summary>
    /// Inventario puro C# (sem MonoBehaviour) -> testavel em EditMode e independente da cena.
    /// Slots fixos no estilo Free Fire: 2 armas + 4 utilitarios (municao/kit/colete/granada).
    /// </summary>
    public sealed class InventorySystem
    {
        public struct Slot
        {
            public ItemDefinition Item;
            public int Amount;

            public bool IsEmpty => Item == null || Amount <= 0;
        }

        private readonly Slot[] _slots;

        public int Capacity => _slots.Length;
        public event Action Changed;

        public InventorySystem(int capacity)
        {
            _slots = new Slot[Mathf.Max(1, capacity)];
        }

        public Slot GetSlot(int index)
        {
            if (index < 0 || index >= _slots.Length) return default;
            return _slots[index];
        }

        /// <summary>Adiciona item empilhando no slot existente e depois em slot vazio.</summary>
        public bool TryAdd(ItemDefinition item, int amount, out int leftover)
        {
            leftover = amount;
            if (item == null || amount <= 0) return false;

            // 1) tenta empilhar
            for (var i = 0; i < _slots.Length; i++)
            {
                ref var slot = ref _slots[i];
                if (slot.IsEmpty || !ReferenceEquals(slot.Item, item)) continue;
                if (slot.Amount >= item.maxStack) continue;

                var space = item.maxStack - slot.Amount;
                var moved = Mathf.Min(space, leftover);
                slot.Amount += moved;
                leftover -= moved;
                if (leftover <= 0) break;
            }

            // 2) tenta slot vazio
            if (leftover > 0)
            {
                for (var i = 0; i < _slots.Length; i++)
                {
                    ref var slot = ref _slots[i];
                    if (!slot.IsEmpty) continue;

                    var moved = Mathf.Min(item.maxStack, leftover);
                    slot.Item = item;
                    slot.Amount = moved;
                    leftover -= moved;
                    if (leftover <= 0) break;
                }
            }

            if (leftover == amount) return false;

            Changed?.Invoke();
            return true;
        }

        public bool TryAdd(ItemDefinition item, int amount) => TryAdd(item, amount, out _);

        public bool Remove(string itemId, int amount)
        {
            if (string.IsNullOrEmpty(itemId) || amount <= 0) return false;

            var available = Count(itemId);
            if (available < amount) return false;

            var remaining = amount;
            for (var i = 0; i < _slots.Length && remaining > 0; i++)
            {
                ref var slot = ref _slots[i];
                if (slot.IsEmpty || slot.Item.itemId != itemId) continue;

                var taken = Mathf.Min(slot.Amount, remaining);
                slot.Amount -= taken;
                remaining -= taken;

                if (slot.Amount <= 0)
                {
                    slot.Item = null;
                    slot.Amount = 0;
                }
            }

            Changed?.Invoke();
            return true;
        }

        public int Count(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return 0;

            var total = 0;
            for (var i = 0; i < _slots.Length; i++)
            {
                var slot = _slots[i];
                if (!slot.IsEmpty && slot.Item.itemId == itemId)
                    total += slot.Amount;
            }
            return total;
        }

        public bool HasSpaceFor(ItemDefinition item, int amount = 1)
        {
            if (item == null) return false;

            for (var i = 0; i < _slots.Length; i++)
            {
                var slot = _slots[i];
                if (slot.IsEmpty) return true;
                if (ReferenceEquals(slot.Item, item) && slot.Amount + amount <= item.maxStack) return true;
            }
            return false;
        }

        public void Clear()
        {
            for (var i = 0; i < _slots.Length; i++) _slots[i] = default;
            Changed?.Invoke();
        }
    }
}
