using System;
using System.Globalization;
using System.Collections.Generic;
using Nekoyume.Helper;
using Nekoyume.UI.Model;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Nekoyume.UI.Module
{
    using System.Threading;
    using System.Threading.Tasks;
    using UniRx;

    public class ShopItemView : CountableItemView<ShopItem>
    {
        public GameObject priceGroup;
        public TextMeshProUGUI priceText;
        [SerializeField] private GameObject expired;

        private readonly List<IDisposable> _disposables = new List<IDisposable>();
        private long _expiredBlockIndex;

        public Task<Nekoyume.Model.Item.ItemBase> ItemBaseLoadingTask { get; private set; } = null;
        private CancellationTokenSource _cancellationTokenSource = null;

        public override void SetData(ShopItem model)
        {
            if (model is null)
            {
                Clear();
                return;
            }

            base.SetData(model);
            SetBg(1f);
            SetLevel(model.ItemBase.Value.Grade, model.Level.Value);
            priceGroup.SetActive(true);

            // [TEN Code Block Start]
            if (model.Count.Value > 1) {
                decimal price = 0;
                if (decimal.TryParse(model.Price.Value.GetQuantityString(), NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out var result))
                {
                    price = result;
                }
                var unitPrice = System.Math.Round(price / model.Count.Value, 7);

                priceText.text = $"{model.Price.Value.GetQuantityString()}({unitPrice})";
            } else {
                priceText.text = model.Price.Value.GetQuantityString();
            }
            // [TEN Code Block END]

            Model.View = this;

            if (expired)
            {
                _expiredBlockIndex = model.ExpiredBlockIndex.Value;
                SetExpired(Game.Game.instance.Agent.BlockIndex);
                Game.Game.instance.Agent.BlockIndexSubject
                    .Subscribe(SetExpired)
                    .AddTo(_disposables);
            }

            _cancellationTokenSource = new CancellationTokenSource();
            ItemBaseLoadingTask = Task.Run(async () =>
            {
                var item = await Util.GetItemBaseByTradableId(model.TradableId.Value, model.ExpiredBlockIndex.Value);
                return item;
            }, _cancellationTokenSource.Token);

            ItemBaseLoadingTask.ToObservable()
                .ObserveOnMainThread()
                .First()
                .Subscribe(item => SetOptionTag(item))
                .AddTo(_disposables);
        }

        public override void Clear()
        {
            if (Model != null)
            {
                Model.Selected.Value = false;
            }

            base.Clear();

            SetBg(0f);
            SetLevel(0, 0);
            priceGroup.SetActive(false);
            if (expired != null)
            {
                expired.SetActive(false);
            }
            _disposables.DisposeAllAndClear();
            _cancellationTokenSource?.Cancel();
        }

        private void SetBg(float alpha)
        {
            var a = alpha;
            var color = backgroundImage.color;
            color.a = a;
            backgroundImage.color = color;
        }

        private void SetLevel(int grade, int level)
        {
            if (level > 0)
            {
                enhancementText.text = $"+{level}";
                enhancementText.enabled = true;
            }

            if (level >= Util.VisibleEnhancementEffectLevel)
            {
                var data = itemViewData.GetItemViewData(grade);
                enhancementImage.GetComponent<Image>().material = data.EnhancementMaterial;
                enhancementImage.SetActive(true);
            }
        }

        private void SetExpired(long blockIndex)
        {
            if (expired)
            {
                expired.SetActive(_expiredBlockIndex - blockIndex <= 0);
            }
        }
    }
}
