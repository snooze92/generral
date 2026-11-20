import { render, screen } from '@testing-library/react';
import React from 'react';
import { describe, expect, it, vi } from 'vitest';
import FormInput from 'Components/Form/FormInput';
import { inputTypes } from 'Helpers/Props';

describe('FormInput', () => {
  it('renders a text input', () => {
    render(
      <FormInput
        type={inputTypes.TEXT}
        name="instanceName"
        value="Generral"
        onChange={vi.fn()}
      />
    );

    expect(screen.getByDisplayValue('Generral')).toHaveProperty('type', 'text');
  });

  it('falls back to a text input for input types Generral does not ship', () => {
    render(
      <FormInput
        type={inputTypes.PATH}
        name="rootFolder"
        value="/tv"
        onChange={vi.fn()}
      />
    );

    expect(screen.getByDisplayValue('/tv')).toHaveProperty('name', 'rootFolder');
  });

  it('shows validation errors', () => {
    render(
      <FormInput
        type={inputTypes.NUMBER}
        name="port"
        value={0}
        errors={[{ message: 'Port must be between 1 and 65535' }]}
        onChange={vi.fn()}
      />
    );

    expect(screen.getByText('Port must be between 1 and 65535')).toBeTruthy();
  });
});
