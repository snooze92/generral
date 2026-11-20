// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/Components/Page/Header/PageHeaderActionsMenu.tsx
import React, { useCallback } from 'react';
import Icon from 'Components/Icon';
import Menu from 'Components/Menu/Menu';
import MenuButton from 'Components/Menu/MenuButton';
import MenuContent from 'Components/Menu/MenuContent';
import MenuItem from 'Components/Menu/MenuItem';
import MenuItemSeparator from 'Components/Menu/MenuItemSeparator';
import { align, icons } from 'Helpers/Props';
import { useSystemStatusData } from 'System/Status/useSystemStatus';
import { useRestart } from 'System/useSystem';
import translate from 'Utilities/String/translate';
import styles from './PageHeaderActionsMenu.module.css';

interface PageHeaderActionsMenuProps {
  onKeyboardShortcutsPress(): void;
}

function PageHeaderActionsMenu({
  onKeyboardShortcutsPress,
}: PageHeaderActionsMenuProps) {
  const { authentication } = useSystemStatusData();
  const { mutate: restart } = useRestart();

  const showSignOut = authentication === 'forms';

  const handleRestartPress = useCallback(() => {
    restart();
  }, [restart]);

  return (
    <div>
      <Menu alignMenu={align.RIGHT}>
        <MenuButton
          className={styles.menuButton}
          aria-label={translate('ApplicationActions')}
        >
          <Icon
            name={icons.INTERACTIVE}
            title={translate('ApplicationActions')}
            titleWrapperClassName={styles.menuButtonIcon}
          />
        </MenuButton>

        <MenuContent>
          <MenuItem onPress={onKeyboardShortcutsPress}>
            <Icon className={styles.itemIcon} name={icons.KEYBOARD} />
            {translate('KeyboardShortcuts')}
          </MenuItem>

          <MenuItemSeparator />

          <MenuItem onPress={handleRestartPress}>
            <Icon className={styles.itemIcon} name={icons.RESTART} />
            {translate('Restart')}
          </MenuItem>

          {showSignOut ? (
            <>
              <MenuItemSeparator />

              <MenuItem
                to={`${window.Generral.urlBase}/logout`}
                noRouter={true}
              >
                <Icon className={styles.itemIcon} name={icons.LOGOUT} />
                {translate('Logout')}
              </MenuItem>
            </>
          ) : null}
        </MenuContent>
      </Menu>
    </div>
  );
}

export default PageHeaderActionsMenu;
