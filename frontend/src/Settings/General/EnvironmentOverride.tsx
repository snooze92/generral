import React, { createContext, useContext } from 'react';
import FormInputHelpText from 'Components/Form/FormInputHelpText';
import translate from 'Utilities/String/translate';

const EnvironmentOverrideContext = createContext<string[]>([]);

export const EnvironmentOverrideProvider = EnvironmentOverrideContext.Provider;

/** True when the setting is fixed by a GENERRAL__* environment variable. */
export function useIsOverriddenByEnvironment(name: string) {
  return useContext(EnvironmentOverrideContext).includes(name);
}

export function EnvironmentOverrideHelpText({ name }: { name: string }) {
  const isOverridden = useIsOverriddenByEnvironment(name);

  return isOverridden ? (
    <FormInputHelpText text={translate('SetByEnvironmentVariable')} />
  ) : null;
}
