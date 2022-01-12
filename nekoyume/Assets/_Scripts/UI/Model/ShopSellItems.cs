﻿using System.Linq;
using Nekoyume.UI.Module;

namespace Nekoyume.UI.Model
{
    public class ShopSellItems : ShopItems
    {
        protected override void ResetSelectedState()
        {
            foreach (var shopItem in Items.Value.SelectMany(keyValuePair => keyValuePair.Value))
            {
                //note: ShopItem is sometimes null, so we needs to investigate.
                shopItem.Selected.Value = false;
            }
        }

        protected override void OnClickItem(ShopItemView view)
        {
            if (view is null || view == SelectedItemView.Value)
            {
                DeselectItemView();
                return;
            }

            SelectItemView(view);
        }
    }
}
