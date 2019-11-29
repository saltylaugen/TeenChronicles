using System;
using System.Collections.Generic;
using System.Linq;
using Assets.SimpleLocalization;
using Nekoyume.Game;
using Nekoyume.TableData;
using UniRx;
using UnityEngine;

namespace Nekoyume.UI.Model
{
    public class ItemInformationSkill : IDisposable
    {
        public readonly ReactiveProperty<Sprite> iconSprite = new ReactiveProperty<Sprite>();
        public readonly ReactiveProperty<string> name = new ReactiveProperty<string>();
        public readonly ReactiveProperty<string> power = new ReactiveProperty<string>();
        public readonly ReactiveProperty<string> chance = new ReactiveProperty<string>();

        public ItemInformationSkill(MaterialItemSheet.Row itemRow)
        {
            if (!Game.Game.instance.TableSheets.SkillSheet.TryGetValue(itemRow.SkillId, out var skillRow))
            {
                throw new KeyNotFoundException(nameof(itemRow.SkillId));
            }

            iconSprite.Value = skillRow.GetIcon();
            name.Value = skillRow.GetLocalizedName();
            power.Value =
                $"{LocalizationManager.Localize("UI_SKILL_POWER")}: {itemRow.SkillDamageMin} - {itemRow.SkillDamageMax}";
            chance.Value =
                $"{LocalizationManager.Localize("UI_SKILL_CHANCE")}: {itemRow.SkillChanceMin}% - {itemRow.SkillChanceMax}%";
        }

        public ItemInformationSkill(Skill skill)
        {
            iconSprite.Value = skill.skillRow.GetIcon();
            name.Value = skill.skillRow.GetLocalizedName();
            power.Value = $"{LocalizationManager.Localize("UI_SKILL_POWER")}: {skill.power}";
            chance.Value = $"{LocalizationManager.Localize("UI_SKILL_CHANCE")}: {skill.chance}%";
        }
        
        public ItemInformationSkill(BuffSkill skill)
        {
            var powerValue = string.Empty;
            var buffs = skill.skillRow.GetBuffs();
            if (buffs.Count > 0)
            {
                var buff = buffs[0];
                powerValue = buff.StatModifier.ToString();
            }
            
            iconSprite.Value = skill.skillRow.GetIcon();
            name.Value = skill.skillRow.GetLocalizedName();
            power.Value = $"{LocalizationManager.Localize("UI_SKILL_EFFECT")}: {powerValue}";
            chance.Value = $"{LocalizationManager.Localize("UI_SKILL_CHANCE")}: {skill.chance}%";
        }

        public void Dispose()
        {
            iconSprite.Dispose();
            name.Dispose();
            power.Dispose();
            chance.Dispose();
        }
    }
}
