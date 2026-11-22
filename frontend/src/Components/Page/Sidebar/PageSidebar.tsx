// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/Components/Page/Sidebar/PageSidebar.tsx
import classNames from 'classnames';
import React, {
  Fragment,
  useCallback,
  useEffect,
  useMemo,
  useRef,
} from 'react';
import { useLocation } from 'react-router';
import {
  setIsSidebarVisible,
  useAppDimension,
  useAppValue,
} from 'App/appStore';
import { IconName } from 'Components/Icon';
import IconButton from 'Components/Link/IconButton';
import Link from 'Components/Link/Link';
import OverlayScroller from 'Components/Scroller/OverlayScroller';
import Scroller from 'Components/Scroller/Scroller';
import { icons } from 'Helpers/Props';
import dimensions from 'Styles/Variables/dimensions';
import HealthStatus from 'System/Status/Health/HealthStatus';
import translate from 'Utilities/String/translate';
import PageSidebarItem from './PageSidebarItem';
import styles from './PageSidebar.module.css';

const SIDEBAR_WIDTH = dimensions.sidebarWidth;

interface SidebarChild {
  title: string | (() => string);
  to: string;
}

interface SidebarItem {
  iconName?: IconName;
  title: string | (() => string);
  to: string;
  alias?: string;
  statusComponent?: React.ElementType;
  children?: SidebarChild[];
}

interface SidebarGroup {
  label?: () => string;
  items: SidebarItem[];
}

export const GROUPS: SidebarGroup[] = [
  {
    items: [
      {
        iconName: icons.SERIES_CONTINUING,
        title: () => translate('Series'),
        to: '/series',
        children: [{ title: () => translate('AddNew'), to: '/add/new' }],
      },
      {
        iconName: icons.GLOBE,
        title: () => translate('Sources'),
        to: '/sources',
      },
      {
        iconName: icons.CALENDAR,
        title: () => translate('Calendar'),
        to: '/calendar',
      },
      {
        iconName: icons.ACTIVITY,
        title: () => translate('Activity'),
        to: '/activity/queue',
        alias: '/activity',
        children: [
          { title: () => translate('Queue'), to: '/activity/queue' },
          { title: () => translate('History'), to: '/activity/history' },
          { title: () => translate('Blocklist'), to: '/activity/blocklist' },
        ],
      },
      {
        iconName: icons.WARNING,
        title: () => translate('Wanted'),
        to: '/wanted/missing',
        alias: '/wanted',
        children: [
          { title: () => translate('Missing'), to: '/wanted/missing' },
          { title: () => translate('CutoffUnmet'), to: '/wanted/cutoffunmet' },
        ],
      },
    ],
  },
  {
    items: [
      {
        iconName: icons.SETTINGS,
        title: () => translate('Settings'),
        to: '/settings',
        children: [
          {
            title: () => translate('MediaManagement'),
            to: '/settings/mediamanagement',
          },
          { title: () => translate('Profiles'), to: '/settings/profiles' },
          { title: () => translate('Quality'), to: '/settings/quality' },
          { title: () => translate('Indexers'), to: '/settings/indexers' },
          {
            title: () => translate('DownloadClients'),
            to: '/settings/downloadclients',
          },
          { title: () => translate('Connect'), to: '/settings/connect' },
          { title: () => translate('Metadata'), to: '/settings/metadata' },
          { title: () => translate('Tags'), to: '/settings/tags' },
          { title: () => translate('General'), to: '/settings/general' },
          { title: () => translate('Ui'), to: '/settings/ui' },
        ],
      },
      {
        iconName: icons.SYSTEM,
        title: () => translate('System'),
        to: '/system/status',
        alias: '/system',
        statusComponent: HealthStatus,
        children: [
          { title: () => translate('Status'), to: '/system/status' },
          { title: () => translate('Tasks'), to: '/system/tasks' },
          { title: () => translate('Backups'), to: '/system/backups' },
          { title: () => translate('Updates'), to: '/system/updates' },
          { title: () => translate('Events'), to: '/system/events' },
          { title: () => translate('LogFiles'), to: '/system/logs/files' },
        ],
      },
    ],
  },
];

const FLAT_LINKS: SidebarItem[] = GROUPS.flatMap((g) => g.items);

function hasActiveChildLink(link: SidebarItem, pathname: string) {
  return !!link.children?.some((child) => child.to === pathname);
}

function getActiveParent(pathname: string) {
  return (
    FLAT_LINKS.find((link) => {
      if (link.to === pathname) {
        return true;
      }

      if (link.children?.some((child) => pathname.startsWith(child.to))) {
        return true;
      }

      return (
        (link.to !== '/' && pathname.startsWith(link.to)) ||
        (!!link.alias && pathname.startsWith(link.alias))
      );
    })?.to ?? FLAT_LINKS[0].to
  );
}

function PageSidebar() {
  const isSidebarVisible = useAppValue('isSidebarVisible');
  const isSidebarOverlay = useAppDimension('isSidebarOverlay');
  const isSidebarCollapsible = useAppDimension('isSidebarCollapsible');
  const { pathname } = useLocation();
  const sidebarRef = useRef<HTMLElement>(null);

  const activeParent = useMemo(() => getActiveParent(pathname), [pathname]);

  const handleWindowClick = useCallback(
    (event: MouseEvent) => {
      const sidebar = sidebarRef.current;
      const toggleButton = document.getElementById('sidebar-toggle-button');
      const target = event.target;

      if (!sidebar) {
        return;
      }

      if (
        target instanceof Node &&
        !sidebar.contains(target) &&
        !toggleButton?.contains(target) &&
        isSidebarVisible
      ) {
        event.preventDefault();
        event.stopPropagation();
        setIsSidebarVisible({ isSidebarVisible: false });
      }
    },
    [isSidebarVisible]
  );

  const handleItemPress = useCallback(() => {
    if (isSidebarOverlay) {
      setIsSidebarVisible({ isSidebarVisible: false });
    }
  }, [isSidebarOverlay]);

  const handleSidebarClosePress = useCallback(() => {
    setIsSidebarVisible({ isSidebarVisible: false });
  }, []);

  useEffect(() => {
    if (isSidebarOverlay) {
      window.addEventListener('click', handleWindowClick, { capture: true });
    }

    return () => {
      window.removeEventListener('click', handleWindowClick, { capture: true });
    };
  }, [isSidebarOverlay, handleWindowClick]);

  const containerStyle = useMemo(() => {
    if (!isSidebarOverlay) {
      return undefined;
    }

    return {
      transform: `translateX(${isSidebarVisible ? 0 : SIDEBAR_WIDTH * -1}px)`,
    };
  }, [isSidebarOverlay, isSidebarVisible]);

  const ScrollerComponent = isSidebarOverlay ? Scroller : OverlayScroller;

  return (
    <nav
      ref={sidebarRef}
      className={classNames(
        styles.sidebarContainer,
        isSidebarCollapsible &&
          !isSidebarOverlay &&
          !isSidebarVisible &&
          styles.isCollapsed
      )}
      style={containerStyle}
      aria-label={translate('MainNavigation')}
    >
      {isSidebarOverlay ? (
        <div className={styles.sidebarHeader}>
          <div className={styles.logoContainer}>
            <Link className={styles.logoLink} to="/">
              <img
                className={styles.logo}
                src={`${window.Generral.urlBase}/Content/Images/logo.svg`}
                alt=""
              />
              <span className={styles.brandName}>Generral</span>
            </Link>
          </div>

          <IconButton
            className={styles.sidebarCloseButton}
            name={icons.CLOSE}
            aria-label={translate('Close')}
            size={20}
            onPress={handleSidebarClosePress}
          />
        </div>
      ) : null}

      <ScrollerComponent
        className={styles.sidebar}
        scrollDirection="vertical"
        style={{
          height: `calc(${window.innerHeight}px - var(--headerHeight))`,
        }}
      >
        <div>
          {GROUPS.map((group, groupIndex) => {
            const showSeparatorBefore = !group.label && groupIndex > 0;

            return (
              <Fragment key={`group-${groupIndex}`}>
                {showSeparatorBefore ? (
                  <div className={styles.divider} aria-hidden="true" />
                ) : null}

                {group.label ? (
                  <span className={styles.groupLabel}>{group.label()}</span>
                ) : null}

                {group.items.map((link) => {
                  const isActiveParent = activeParent === link.to;
                  const hasActiveChild = hasActiveChildLink(link, pathname);

                  return (
                    <PageSidebarItem
                      key={link.to}
                      iconName={link.iconName}
                      title={link.title}
                      to={link.to}
                      statusComponent={link.statusComponent}
                      isActive={pathname === link.to && !hasActiveChild}
                      isActiveParent={isActiveParent}
                      isParentItem={!!link.children}
                      onPress={handleItemPress}
                    >
                      {link.children && isActiveParent
                        ? link.children.map((child) => {
                            return (
                              <PageSidebarItem
                                key={child.to}
                                title={child.title}
                                to={child.to}
                                isActive={pathname === child.to}
                                isParentItem={false}
                                isChildItem={true}
                                onPress={handleItemPress}
                              />
                            );
                          })
                        : null}
                    </PageSidebarItem>
                  );
                })}
              </Fragment>
            );
          })}
        </div>
      </ScrollerComponent>
    </nav>
  );
}

export default PageSidebar;
