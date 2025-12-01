import pluginVue from 'eslint-plugin-vue'
import vueTsEslintConfig from '@vue/eslint-config-typescript'
import configPrettier from 'eslint-config-prettier'

export default [
  {
    ignores: ['dist/', 'src/services/generated/'],
  },
  ...pluginVue.configs['flat/recommended'],
  ...vueTsEslintConfig(),
  configPrettier,
  {
    rules: {
      'vue/multi-word-component-names': 'off',
      // TypeScript enforces prop types via defineProps<T>(); the runtime rule
      // also fires false positives on mount() call objects in *.spec.ts files.
      'vue/require-prop-types': 'off',
    },
  },
]
