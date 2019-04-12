using System;
using Nekoyume.Data.Table;

namespace Nekoyume.Game.Item
{
    [Serializable]
    public class ItemUsable : ItemBase
    {
        public new ItemEquipment Data { get; }

        public ItemUsable(Data.Table.Item data)
            : base(data)
        {
            Data = (ItemEquipment) data;
        }

        public virtual bool Use()
        {
            return false;
        }

        public override string ToItemInfo()
        {
            return "";
        }
    }
}
