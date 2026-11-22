import React from 'react';
import CheckInput from 'Components/Form/CheckInput';
import IconButton from 'Components/Link/IconButton';
import { icons } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import { QualityProfileItem } from './useQualityProfiles';
import styles from './QualityProfileItems.module.css';

export const itemId = (item: QualityProfileItem) =>
  item.quality ? item.quality.id : (item.id ?? 0);

export const itemName = (item: QualityProfileItem) =>
  item.quality ? item.quality.name : (item.name ?? '');

interface QualityProfileItemsProps {
  /** Lowest to highest preference, as stored; shown highest first like Sonarr. */
  items: QualityProfileItem[];
  onChange: (items: QualityProfileItem[]) => void;
}

/** Checkbox list of the qualities and groups, reordered with up/down buttons. */
function QualityProfileItems({ items, onChange }: QualityProfileItemsProps) {
  const toggle = (index: number, allowed: boolean) => {
    onChange(
      items.map((item, i) =>
        i === index
          ? {
              ...item,
              allowed,
              items: item.items.map((child) => ({ ...child, allowed })),
            }
          : item
      )
    );
  };

  // Moving up in the displayed list means a higher preference, i.e. later in `items`.
  const move = (index: number, offset: number) => {
    const target = index + offset;

    if (target < 0 || target >= items.length) {
      return;
    }

    const reordered = [...items];
    [reordered[index], reordered[target]] = [reordered[target], reordered[index]];
    onChange(reordered);
  };

  const displayed = items.map((item, index) => ({ item, index })).reverse();

  return (
    <ul className={styles.items}>
      {displayed.map(({ item, index }, position) => {
        const name = itemName(item);

        return (
          <li key={itemId(item)} className={styles.item}>
            <CheckInput
              containerClassName={styles.check}
              name={`quality.${itemId(item)}`}
              ariaLabel={name}
              value={item.allowed}
              onChange={({ value }) => toggle(index, value)}
            />

            <span className={styles.label}>
              <span className={styles.name}>{name}</span>

              {item.quality ? null : (
                <span className={styles.groupItems}>
                  {item.items.map(itemName).join(', ')}
                </span>
              )}
            </span>

            <IconButton
              name={icons.SORT_ASCENDING}
              title={translate('MoveUp')}
              isDisabled={position === 0}
              onPress={() => move(index, 1)}
            />

            <IconButton
              name={icons.SORT_DESCENDING}
              title={translate('MoveDown')}
              isDisabled={position === displayed.length - 1}
              onPress={() => move(index, -1)}
            />
          </li>
        );
      })}
    </ul>
  );
}

export default QualityProfileItems;
