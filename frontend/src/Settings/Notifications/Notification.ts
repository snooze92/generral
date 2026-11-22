import { Provider } from 'Settings/Provider/Provider';

export const TRIGGERS = [
  'onGrab',
  'onDownload',
  'onUpgrade',
  'onImportComplete',
  'onRename',
  'onSeriesAdd',
  'onSeriesDelete',
  'onEpisodeFileDelete',
  'onEpisodeFileDeleteForUpgrade',
  'onHealthIssue',
  'onHealthRestored',
  'onApplicationUpdate',
  'onManualInteractionRequired',
] as const;

export type Trigger = (typeof TRIGGERS)[number];

type Supports = {
  [K in Trigger as `supports${Capitalize<K>}`]: boolean;
};

type Triggers = Record<Trigger, boolean>;

interface Notification extends Provider, Triggers, Supports {
  includeHealthWarnings: boolean;
  link?: string;
}

export const PATH = '/notification';

export const supportsKey = (trigger: Trigger) =>
  `supports${trigger[0].toUpperCase()}${trigger.slice(1)}` as keyof Supports;

export default Notification;
