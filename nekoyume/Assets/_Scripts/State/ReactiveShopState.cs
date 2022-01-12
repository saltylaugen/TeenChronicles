using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bencodex.Types;
using Cysharp.Threading.Tasks;
using Lib9c.Model.Order;
using Libplanet;
using Libplanet.Assets;
using Nekoyume.Model.Item;
using Nekoyume.Model.Stat;
using Nekoyume.Model.State;
using Nekoyume.TableData;
using Nekoyume.UI.Model;
using Nekoyume.UI.Module;
using UniRx;
using UnityEngine;

namespace Nekoyume.State
{
    /// <summary>
    /// Changes in the values included in ShopState are notified to the outside through each ReactiveProperty<T> field.
    /// </summary>
    public static class ReactiveShopState
    {
        private enum SortType
        {
            None = 0,
            Grade = 1,
            Cp = 2,
        }

        private static readonly List<ItemSubType> ItemSubTypes = new List<ItemSubType>()
        {
            ItemSubType.Weapon,
            ItemSubType.Armor,
            ItemSubType.Belt,
            ItemSubType.Necklace,
            ItemSubType.Ring,
            ItemSubType.Food,
            ItemSubType.FullCostume,
            ItemSubType.HairCostume,
            ItemSubType.EarCostume,
            ItemSubType.EyeCostume,
            ItemSubType.TailCostume,
            ItemSubType.Title,
            ItemSubType.Hourglass,
            ItemSubType.ApStone,
        };

        private static readonly List<ItemSubType> ShardedSubTypes = new List<ItemSubType>()
        {
            ItemSubType.Weapon,
            ItemSubType.Armor,
            ItemSubType.Belt,
            ItemSubType.Necklace,
            ItemSubType.Ring,
            ItemSubType.Food,
            ItemSubType.Hourglass,
            ItemSubType.ApStone,
        };

        public static readonly
            ReactiveProperty<IReadOnlyDictionary<ItemSubTypeFilter,
                Dictionary<ShopSortFilter, Dictionary<int, List<OrderDigest>>>>> BuyDigests =
                new ReactiveProperty<IReadOnlyDictionary<ItemSubTypeFilter,
                    Dictionary<ShopSortFilter, Dictionary<int, List<OrderDigest>>>>>();

        public static readonly
            ReactiveProperty<IReadOnlyDictionary<ItemSubTypeFilter,
                Dictionary<ShopSortFilter, Dictionary<int, List<OrderDigest>>>>> SellDigests =
                new ReactiveProperty<IReadOnlyDictionary<ItemSubTypeFilter,
                    Dictionary<ShopSortFilter, Dictionary<int, List<OrderDigest>>>>>();

        private static List<OrderDigest> _buyDigests = new List<OrderDigest>();
        private static List<OrderDigest> _sellDigests = new List<OrderDigest>();


        // key: orderId
        private static ConcurrentDictionary<Guid, ItemBase> CachedShopItems { get; } = new ConcurrentDictionary<Guid, ItemBase>();


        public static OrderDigest GetSellDigest(Guid tradableId,
            long requiredBlockIndex,
            FungibleAssetValue price,
            int count)
        {
            return _sellDigests.FirstOrDefault(x =>
                x.TradableId.Equals(tradableId) &&
                x.ExpiredBlockIndex.Equals(requiredBlockIndex) &&
                x.Price.Equals(price) &&
                x.ItemCount.Equals(count));
        }

        private const int buyItemsPerPage = 24;
        private const int sellItemsPerPage = 20;

        public static async Task InitAndUpdateBuyDigests()
        {
            _buyDigests = await GetBuyOrderDigests();
            var result = await UpdateCachedShopItems(_buyDigests);
            if (result)
            {
                UpdateBuyDigests();
            }
        }

        public static async void InitSellDigests()
        {
            if (_sellDigests != null)
            {
                await UniTask.Run(async () =>
                {
                    _sellDigests = await GetSellOrderDigests();
                    await UpdateCachedShopItems(_sellDigests);
                });
            }
        }

        public static async void InitAndUpdateSellDigests()
        {
            _sellDigests = await GetSellOrderDigests();
            var result = await UpdateCachedShopItems(_sellDigests);
            if (result)
            {
                UpdateSellDigests();
            }
        }

        private static async Task<bool> UpdateCachedShopItems(IEnumerable<OrderDigest> digests)
        {
            var selectedDigests = digests.Where(orderDigest => !CachedShopItems.ContainsKey(orderDigest.OrderId)).ToList();
            var tuples = selectedDigests
                .Select(e => (Address: Addresses.GetItemAddress(e.TradableId), OrderDigest: e))
                .ToArray();
            var itemAddresses = tuples.Select(tuple => tuple.Address).Distinct();
            var itemValues = await Game.Game.instance.Agent.GetStateBulk(itemAddresses);
            foreach (var (address, orderDigest) in tuples)
            {
                if (!itemValues.ContainsKey(address))
                {
                    Debug.LogWarning($"[{nameof(ReactiveShopState)}] Not found address: {address.ToHex()}");
                    continue;
                }

                var itemValue = itemValues[address];
                if (!(itemValue is Dictionary dictionary))
                {
                    Debug.LogWarning($"[{nameof(ReactiveShopState)}] {nameof(itemValue)} cannot cast to {typeof(Bencodex.Types.Dictionary).FullName}");
                    continue;
                }

                var itemBase = ItemFactory.Deserialize(dictionary);
                switch (itemBase)
                {
                    case TradableMaterial tm:
                        tm.RequiredBlockIndex = orderDigest.ExpiredBlockIndex;
                        break;
                    case ItemUsable iu:
                        iu.RequiredBlockIndex = orderDigest.ExpiredBlockIndex;
                        break;
                    case Costume c:
                        c.RequiredBlockIndex = orderDigest.ExpiredBlockIndex;
                        break;
                }
                CachedShopItems.TryAdd(orderDigest.OrderId, itemBase);
            }
            return true;
        }

        private static void UpdateBuyDigests()
        {
            var buyDigests = _buyDigests.Where(digest =>
                !digest.SellerAgentAddress.Equals(States.Instance.AgentState.address)).ToList();
            BuyDigests.Value =
                GetGroupedOrderDigestsByItemSubTypeFilter(buyDigests, buyItemsPerPage);
        }

        private static void UpdateSellDigests()
        {
            SellDigests.Value =
                GetGroupedOrderDigestsByItemSubTypeFilter(_sellDigests, sellItemsPerPage);
        }

        public static void RemoveBuyDigest(Guid orderId)
        {
            var item = _buyDigests.FirstOrDefault(x => x.OrderId.Equals(orderId));
            if (item != null)
            {
                _buyDigests.Remove(item);
            }

            UpdateBuyDigests();
        }

        public static void RemoveSellDigest(Guid orderId)
        {
            var item = _sellDigests.FirstOrDefault(x => x.OrderId.Equals(orderId));
            if (item != null)
            {
                _sellDigests.Remove(item);
            }

            UpdateSellDigests();
        }

        public static bool TryGetShopItem(OrderDigest orderDigest, out ItemBase itemBase)
        {
            if (!CachedShopItems.ContainsKey(orderDigest.OrderId))
            {
                Debug.LogWarning($"[{nameof(TryGetShopItem)}] Not found address: {orderDigest.OrderId}");
                itemBase = null;
                return false;
            }

            itemBase = CachedShopItems[orderDigest.OrderId];
            return true;
        }

        private static
            Dictionary<ItemSubTypeFilter,
                Dictionary<ShopSortFilter, Dictionary<int, List<OrderDigest>>>>
            GetGroupedOrderDigestsByItemSubTypeFilter(IReadOnlyCollection<OrderDigest> digests,
                int shopItemsPerPage)
        {
            var weapons = new List<OrderDigest>();
            var armors = new List<OrderDigest>();
            var belts = new List<OrderDigest>();
            var necklaces = new List<OrderDigest>();
            var rings = new List<OrderDigest>();
            var foodsHp = new List<OrderDigest>();
            var foodsAtk = new List<OrderDigest>();
            var foodsDef = new List<OrderDigest>();
            var foodsCri = new List<OrderDigest>();
            var foodsHit = new List<OrderDigest>();
            var fullCostumes = new List<OrderDigest>();
            var hairCostumes = new List<OrderDigest>();
            var earCostumes = new List<OrderDigest>();
            var eyeCostumes = new List<OrderDigest>();
            var tailCostumes = new List<OrderDigest>();
            var titles = new List<OrderDigest>();
            var materials = new List<OrderDigest>();

            foreach (var digest in digests)
            {
                var itemId = digest.ItemId;
                var row = Game.Game.instance.TableSheets.ItemSheet[itemId];
                var itemSubType = row.ItemSubType;

                if (itemSubType == ItemSubType.Food)
                {
                    var consumableRow = (ConsumableItemSheet.Row) row;
                    var state = consumableRow.Stats.First();
                    switch (state.StatType)
                    {
                        case StatType.HP:
                            foodsHp.Add(digest);
                            break;
                        case StatType.ATK:
                            foodsAtk.Add(digest);
                            break;
                        case StatType.DEF:
                            foodsDef.Add(digest);
                            break;
                        case StatType.CRI:
                            foodsCri.Add(digest);
                            break;
                        case StatType.HIT:
                            foodsHit.Add(digest);
                            break;
                    }
                }
                else
                {
                    switch (row.ItemSubType)
                    {
                        case ItemSubType.Weapon:
                            weapons.Add(digest);
                            break;
                        case ItemSubType.Armor:
                            armors.Add(digest);
                            break;
                        case ItemSubType.Belt:
                            belts.Add(digest);
                            break;
                        case ItemSubType.Necklace:
                            necklaces.Add(digest);
                            break;
                        case ItemSubType.Ring:
                            rings.Add(digest);
                            break;
                        case ItemSubType.FullCostume:
                            fullCostumes.Add(digest);
                            break;
                        case ItemSubType.HairCostume:
                            hairCostumes.Add(digest);
                            break;
                        case ItemSubType.EarCostume:
                            earCostumes.Add(digest);
                            break;
                        case ItemSubType.EyeCostume:
                            eyeCostumes.Add(digest);
                            break;
                        case ItemSubType.TailCostume:
                            tailCostumes.Add(digest);
                            break;
                        case ItemSubType.Title:
                            titles.Add(digest);
                            break;
                        case ItemSubType.Hourglass:
                        case ItemSubType.ApStone:
                            materials.Add(digest);
                            break;
                    }
                }
            }

            var groupedOrderDigests =
                new
                    Dictionary<ItemSubTypeFilter,
                        Dictionary<ShopSortFilter, Dictionary<int, List<OrderDigest>>>>
                    {
                        { ItemSubTypeFilter.All, GetGroupedOrderDigestsBySortFilter(digests, shopItemsPerPage) },
                        { ItemSubTypeFilter.Weapon, GetGroupedOrderDigestsBySortFilter(weapons, shopItemsPerPage) },
                        { ItemSubTypeFilter.Armor, GetGroupedOrderDigestsBySortFilter(armors, shopItemsPerPage) },
                        { ItemSubTypeFilter.Belt, GetGroupedOrderDigestsBySortFilter(belts, shopItemsPerPage) },
                        { ItemSubTypeFilter.Necklace, GetGroupedOrderDigestsBySortFilter(necklaces, shopItemsPerPage) },
                        { ItemSubTypeFilter.Ring, GetGroupedOrderDigestsBySortFilter(rings, shopItemsPerPage) },
                        { ItemSubTypeFilter.Food_HP, GetGroupedOrderDigestsBySortFilter(foodsHp, shopItemsPerPage) },
                        { ItemSubTypeFilter.Food_ATK, GetGroupedOrderDigestsBySortFilter(foodsAtk, shopItemsPerPage) },
                        { ItemSubTypeFilter.Food_DEF, GetGroupedOrderDigestsBySortFilter(foodsDef, shopItemsPerPage) },
                        { ItemSubTypeFilter.Food_CRI, GetGroupedOrderDigestsBySortFilter(foodsCri, shopItemsPerPage) },
                        { ItemSubTypeFilter.Food_HIT, GetGroupedOrderDigestsBySortFilter(foodsHit, shopItemsPerPage) },
                        { ItemSubTypeFilter.FullCostume, GetGroupedOrderDigestsBySortFilter(fullCostumes, shopItemsPerPage) },
                        { ItemSubTypeFilter.HairCostume, GetGroupedOrderDigestsBySortFilter(hairCostumes, shopItemsPerPage) },
                        { ItemSubTypeFilter.EarCostume, GetGroupedOrderDigestsBySortFilter(earCostumes, shopItemsPerPage) },
                        { ItemSubTypeFilter.EyeCostume, GetGroupedOrderDigestsBySortFilter(eyeCostumes, shopItemsPerPage) },
                        { ItemSubTypeFilter.TailCostume, GetGroupedOrderDigestsBySortFilter(tailCostumes, shopItemsPerPage) },
                        { ItemSubTypeFilter.Title, GetGroupedOrderDigestsBySortFilter(titles, shopItemsPerPage) },
                        { ItemSubTypeFilter.Materials, GetGroupedOrderDigestsBySortFilter(materials, shopItemsPerPage) },
                    };
            return groupedOrderDigests;
        }

        private static Dictionary<ShopSortFilter, Dictionary<int, List<OrderDigest>>>
            GetGroupedOrderDigestsBySortFilter(IReadOnlyCollection<OrderDigest> digests,
                int shopItemsPerPage)
        {
            return new Dictionary<ShopSortFilter, Dictionary<int, List<OrderDigest>>>
            {
                {
                    ShopSortFilter.Class,
                    GetGroupedShopItemsByPage(GetSortedOrderDigests(digests, SortType.Grade),
                        shopItemsPerPage)
                },
                {
                    ShopSortFilter.CP,
                    GetGroupedShopItemsByPage(GetSortedOrderDigests(digests, SortType.Cp),
                        shopItemsPerPage)
                },
                {
                    ShopSortFilter.Price,
                    GetGroupedShopItemsByPage(
                        digests.OrderByDescending(digest => digest.Price).ToList(),
                        shopItemsPerPage)
                },
            };
        }

        private static List<OrderDigest> GetSortedOrderDigests(IEnumerable<OrderDigest> digests,
            SortType type)
        {
            var result = new List<OrderDigest>();
            var costumeSheet = Game.Game.instance.TableSheets.CostumeItemSheet;
            var materialSheet = Game.Game.instance.TableSheets.MaterialItemSheet;
            var costumes = new List<OrderDigest>();
            var materials = new List<OrderDigest>();
            var itemUsables = new List<OrderDigest>();

            foreach (var digest in digests)
            {
                if (costumeSheet.ContainsKey(digest.ItemId))
                {
                    costumes.Add(digest);
                }
                else if (materialSheet.ContainsKey(digest.ItemId))
                {
                    materials.Add(digest);
                }
                else
                {
                    itemUsables.Add(digest);
                }
            }

            result.AddRange(costumes.OrderByDescending(digest => GetTypeValue(digest, type)));
            result.AddRange(itemUsables.OrderByDescending(digest => GetTypeValue(digest, type)));
            result.AddRange(materials.OrderByDescending(digest => GetTypeValue(digest, type)));
            return result;
        }

        private static int GetTypeValue(OrderDigest item, SortType type)
        {
            switch (type)
            {
                case SortType.Grade:
                    return Game.Game.instance.TableSheets.ItemSheet[item.ItemId].Grade;
                case SortType.Cp:
                    return item.CombatPoint;
                case SortType.None:
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, null);
            }

            throw new ArgumentOutOfRangeException(nameof(type), type, null);
        }

        private static Dictionary<int, List<OrderDigest>> GetGroupedShopItemsByPage(
            List<OrderDigest> digests,
            int shopItemsPerPage)
        {
            var result = new Dictionary<int, List<OrderDigest>>();
            var remainCount = digests.Count;
            var listIndex = 0;
            var pageIndex = 0;
            while (remainCount > 0)
            {
                var getCount = Math.Min(shopItemsPerPage, remainCount);
                var getList = digests.GetRange(listIndex, getCount);
                result.Add(pageIndex, getList);
                remainCount -= getCount;
                listIndex += getCount;
                pageIndex++;
            }

            return result;
        }

        private static async Task<List<OrderDigest>> GetBuyOrderDigests()
        {
            var orderDigests = new Dictionary<Address, List<OrderDigest>>();
            var addressList = new List<Address>();

            foreach (var itemSubType in ItemSubTypes)
            {
                if (ShardedSubTypes.Contains(itemSubType))
                {
                    addressList.AddRange(ShardedShopState.AddressKeys.Select(addressKey =>
                        ShardedShopStateV2.DeriveAddress(itemSubType, addressKey)));
                }
                else
                {
                    var address = ShardedShopStateV2.DeriveAddress(itemSubType, string.Empty);
                    addressList.Add(address);
                }
            }

            Dictionary<Address, IValue> values = await Game.Game.instance.Agent.GetStateBulk(addressList);
            var shopStates = new List<ShardedShopStateV2>();
            foreach (var kv in values)
            {
                if (kv.Value is Dictionary shopDict)
                {
                    shopStates.Add(new ShardedShopStateV2(shopDict));
                }
            }

            AddOrderDigest(shopStates, orderDigests);

            var digests = new List<OrderDigest>();
            foreach (var items in orderDigests.Values)
            {
                digests.AddRange(items);
            }
            return digests;
        }

        private static void AddOrderDigest(List<ShardedShopStateV2> shopStates,
            IDictionary<Address, List<OrderDigest>> orderDigests)
        {
            foreach (var shopState in shopStates)
            {
                foreach (var orderDigest in shopState.OrderDigestList)
                {
                    if (orderDigest.ExpiredBlockIndex != 0 && orderDigest.ExpiredBlockIndex >
                        Game.Game.instance.Agent.BlockIndex)
                    {
                        var agentAddress = orderDigest.SellerAgentAddress;
                        if (!orderDigests.ContainsKey(agentAddress))
                        {
                            orderDigests[agentAddress] = new List<OrderDigest>();
                        }
                        orderDigests[agentAddress].Add(orderDigest);
                    }
                }
            }
        }

        private static async Task<List<OrderDigest>> GetSellOrderDigests()
        {
            var avatarAddress = States.Instance.CurrentAvatarState.address;
            var receiptAddress = OrderDigestListState.DeriveAddress(avatarAddress);
            var receiptState = await Game.Game.instance.Agent.GetStateAsync(receiptAddress);
            var receipts = new List<OrderDigest>();
            if (receiptState is Dictionary dictionary)
            {
                var state = new OrderDigestListState(dictionary);

                var validOrderDigests = state.OrderDigestList.Where(x =>
                    x.ExpiredBlockIndex > Game.Game.instance.Agent.BlockIndex);
                receipts.AddRange(validOrderDigests);

                var expiredOrderDigests = state.OrderDigestList.Where(x =>
                    x.ExpiredBlockIndex <= Game.Game.instance.Agent.BlockIndex);
                var inventory = States.Instance.CurrentAvatarState.inventory;
                var lockedDigests = expiredOrderDigests
                    .Where(x => inventory.TryGetLockedItem(new OrderLock(x.OrderId), out _))
                    .ToList();
                receipts.AddRange(lockedDigests);
            }

            return receipts;
        }
    }
}
