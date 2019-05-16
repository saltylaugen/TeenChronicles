using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Libplanet;
using Libplanet.Action;
using Nekoyume.Data;
using Nekoyume.Data.Table;
using Nekoyume.Game.Item;
using UniRx;
using UnityEngine;

namespace Nekoyume.Action
{
    [ActionType("combination")]
    public class Combination : GameAction
    {
        [Serializable]
        public struct ItemModel
        {
            public int id;
            public int count;

            public ItemModel(int id, int count)
            {
                this.id = id;
                this.count = count;
            }

            public ItemModel(UI.Model.CountableItem item)
            {
                id = item.item.Value.Data.id;
                count = item.count.Value;
                Debug.Log($"ItemModel | Id:{id}, Count:{count}");
            }
        }

        private struct ItemModelInventoryItemPair
        {
            public readonly ItemModel ItemModel;
            public readonly Inventory.InventoryItem InventoryItem;

            public ItemModelInventoryItemPair(ItemModel itemModel, Inventory.InventoryItem inventoryItem)
            {
                ItemModel = itemModel;
                InventoryItem = inventoryItem;
            }
        }

        public struct ResultModel
        {
            public int ErrorCode;
            public ItemModel Item;
        }

        public static readonly Subject<Combination> EndOfExecuteSubject = new Subject<Combination>();

        public List<ItemModel> Materials { get; private set; }
        public ResultModel Result { get; private set; }

        protected override IImmutableDictionary<string, object> PlainValueInternal =>
            new Dictionary<string, object>
            {
                ["Materials"] = ByteSerializer.Serialize(Materials),
            }.ToImmutableDictionary();

        public Combination()
        {
            Materials = new List<ItemModel>();
        }

        protected override void LoadPlainValueInternal(IImmutableDictionary<string, object> plainValue)
        {
            Materials = ByteSerializer.Deserialize<List<ItemModel>>((byte[]) plainValue["Materials"]);
        }

        protected override IAccountStateDelta ExecuteInternal(IActionContext actionCtx)
        {
            var states = actionCtx.PreviousStates;
            var ctx = (Context) states.GetState(actionCtx.Signer);
            if (actionCtx.Rehearsal)
            {
                if (ReferenceEquals(ctx, null))
                {
                    ctx = CreateNovice.CreateContext("dummy", default(Address));
                }
                return states.SetState(actionCtx.Signer, ctx);
            }

            // 인벤토리에 재료를 갖고 있는지 검증.
            var pairs = new List<ItemModelInventoryItemPair>();
            for (var i = Materials.Count - 1; i >= 0; i--)
            {
                var m = Materials[i];
                try
                {
                    var inventoryItem =
                        ctx.avatar.Items.First(item => item.Item.Data.id == m.id && item.Count >= m.count);
                    pairs.Add(new ItemModelInventoryItemPair(m, inventoryItem));
                }
                catch (InvalidOperationException)
                {
                    Result = new ResultModel() {ErrorCode = GameActionResult.ErrorCode.Fail};
                    EndOfExecuteSubject.OnNext(this);

                    return states.SetState(actionCtx.Signer, ctx);
                }
            }

            // 조합식 테이블 로드.
            var recipeTable = Tables.instance.Recipe;

            // 조합식 검증.
            Recipe resultItem = null;
            var resultCount = 0;
            using (var e = recipeTable.GetEnumerator())
            {
                while (e.MoveNext())
                {
                    if (!e.Current.Value.IsMatch(Materials))
                    {
                        continue;
                    }

                    resultItem = e.Current.Value;
                    resultCount = e.Current.Value.CalculateCount(Materials);
                    break;
                }
            }

            // 제거가 잘 됐는지 로그 찍기.
            ctx.avatar.Items.ForEach(item => Debug.Log($"제거 전 // Id:{item.Item.Data.id}, Count:{item.Count}"));
            
            // 사용한 재료를 인벤토리에서 제거.
            pairs.ForEach(pair =>
            {
                Debug.Log($"제거 // pair.InventoryItem.Count:{pair.InventoryItem.Count}, pair.ItemModel.Count:{pair.ItemModel.count}");
                pair.InventoryItem.Count -= pair.ItemModel.count;
                if (pair.InventoryItem.Count == 0)
                {
                    ctx.avatar.Items.Remove(pair.InventoryItem);
                }
            });
            
            // 제거가 잘 됐는지 로그 찍기.
            ctx.avatar.Items.ForEach(item => Debug.Log($"제거 후 // Id:{item.Item.Data.id}, Count:{item.Count}"));

            // 뽀각!!
            if (ReferenceEquals(resultItem, null) ||
                resultCount == 0)
            {
                Result = new ResultModel() {ErrorCode = GameActionResult.ErrorCode.Fail};
                EndOfExecuteSubject.OnNext(this);

                return states.SetState(actionCtx.Signer, ctx);
            }
            
            // 조합 결과 획득.
            {
                var itemTable = Tables.instance.ItemEquipment;
                ItemEquipment itemData;
                if (itemTable.TryGetValue(resultItem.Id, out itemData))
                {
                    try
                    {
                        var inventoryItem = ctx.avatar.Items.First(item => item.Item.Data.id == resultItem.Id);
                        inventoryItem.Count += resultCount;
                    }
                    catch (InvalidOperationException)
                    {
                        var itemBase = ItemBase.ItemFactory(itemData);
                        ctx.avatar.Items.Add(new Inventory.InventoryItem(itemBase, resultCount));   
                    }
                }
                else
                {
                    Result = new ResultModel() {ErrorCode = GameActionResult.ErrorCode.KeyNotFoundInTable};
                    EndOfExecuteSubject.OnNext(this);
                    
                    return states.SetState(actionCtx.Signer, ctx);
                }
            }
            
            // 획득이 잘 됐는지 로그 찍기.
            ctx.avatar.Items.ForEach(item => Debug.Log($"획득 후 // Id:{item.Item.Data.id}, Count:{item.Count}"));

            Result = new ResultModel()
            {
                ErrorCode = GameActionResult.ErrorCode.Success,
                Item = new ItemModel(resultItem.Id, resultCount)
            };
            EndOfExecuteSubject.OnNext(this);

            ctx.updatedAt = DateTimeOffset.UtcNow;
            return states.SetState(actionCtx.Signer, ctx);
        }
    }
}
