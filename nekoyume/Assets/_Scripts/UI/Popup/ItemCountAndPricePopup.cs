using System;
using System.Collections.Generic;
using UniRx;
using UnityEngine.UI;

namespace Nekoyume.UI
{
    public class ItemCountAndPricePopup : ItemCountPopup<Model.ItemCountAndPricePopup>
    {
        public InputField priceInputField;
        
        private Model.ItemCountAndPricePopup _data;
        private readonly List<IDisposable> _disposablesForAwake = new List<IDisposable>();
        private readonly List<IDisposable> _disposablesForSetData = new List<IDisposable>();
        
        #region Mono

        protected override void Awake()
        {
            base.Awake();
            
            this.ComponentFieldsNotNullTest();

            priceInputField.onValueChanged.AsObservable()
                .Subscribe(_ =>
                {
                    if (!int.TryParse(_, out var price))
                    {
                        price = 0;
                    }
                    _data.price.Value = price;
                }).AddTo(_disposablesForAwake);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            
            _disposablesForAwake.DisposeAllAndClear();
            Clear();
        }
        
        #endregion

        public override void Pop(Model.ItemCountAndPricePopup data)
        {
            base.Pop(data);
            
            if (ReferenceEquals(data, null))
            {
                return;
            }

            SetData(data);
        }

        private void SetData(Model.ItemCountAndPricePopup data)
        {
            if (ReferenceEquals(data, null))
            {
                Clear();
                return;
            }
            
            _disposablesForSetData.DisposeAllAndClear();
            _data = data;
            _data.priceInteractable.Subscribe(interactable => priceInputField.interactable = interactable).AddTo(_disposablesForSetData);

            UpdateView();
        }
        
        private void Clear()
        {
            _disposablesForSetData.DisposeAllAndClear();
            _data = null;

            UpdateView();
        }

        private void UpdateView()
        {
            if (ReferenceEquals(_data, null))
            {
                return;
            }

            priceInputField.text = _data.price.Value.ToString("N0");
            priceInputField.interactable = _data.priceInteractable.Value;
        }
    }
}
