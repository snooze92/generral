import ModelBase from 'App/ModelBase';
import { Kind } from 'Helpers/Props/kinds';
import Field from 'typings/Field';

export interface ProviderSelectOption {
  value: string | number;
  name: string;
  hint?: string;
}

export interface ProviderField extends Omit<Field, 'value'> {
  value?: unknown;
  helpText?: string;
  unit?: string;
  placeholder?: string;
  isFloat?: boolean;
  selectOptions?: ProviderSelectOption[];
}

export interface ProviderMessage {
  message: string;
  type: Extract<Kind, 'info' | 'error' | 'warning'>;
}

/** A model with a `fields[]` schema: sources, indexers and download clients. */
export interface ProviderBase extends ModelBase {
  name: string;
  fields: ProviderField[];
}

/** Sonarr's provider resource (indexers, download clients). */
export interface Provider extends ProviderBase {
  implementation: string;
  implementationName: string;
  configContract: string;
  infoLink?: string;
  message?: ProviderMessage;
  tags: number[];
}
