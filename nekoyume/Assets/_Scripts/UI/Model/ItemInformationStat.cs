using System;
using Nekoyume.EnumType;
using Nekoyume.Game;
using Nekoyume.TableData;
using UniRx;

namespace Nekoyume.UI.Model
{
    public class ItemInformationStat : IDisposable
    {
        public readonly ReactiveProperty<string> key = new ReactiveProperty<string>();
        public readonly ReactiveProperty<string> value = new ReactiveProperty<string>();

        public ItemInformationStat(MaterialItemSheet.Row itemRow)
        {
            key.Value = itemRow.StatType != StatType.NONE
                ? itemRow.StatType.Value.GetLocalizedString()
                : $"{nameof(itemRow.StatType)} has not value";
            value.Value = $"{itemRow.StatMin} - {itemRow.StatMax}";
        }

        public ItemInformationStat(StatMapEx statMapEx)
        {
            key.Value = statMapEx.StatType.GetLocalizedString();
            value.Value = $"{statMapEx.TotalValueAsInt}";
        }

        public void Dispose()
        {
            key.Dispose();
            value.Dispose();
        }
    }
}
