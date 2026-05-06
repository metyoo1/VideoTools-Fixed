import re

with open(r'd:\Tool\视频处理\VideoTools-1.4.0\VideoTools-1.4.0\VideoTools\MainWindow.xaml.cs', 'r', encoding='utf-8') as f:
    content = f.read()

# 1. Replace null-conditional operator ?. with manual null checks
# Pattern: expr?.Member -> (expr == null ? null : expr.Member)
# But this is complex. Let's do simple replacements for common patterns.

# button?.Tag?.ToString()?.ToLower() ?? ".gif"
def replace_null_conditional(match):
    expr = match.group(1)
    chain = match.group(2)
    default_val = match.group(3)
    # Build nested ternary
    # For simple cases like button?.Tag?.ToString()?.ToLower() ?? ".gif"
    parts = chain.split('?.')
    result = expr
    for part in parts[1:]:
        result += '.' + part
    # This is tricky. Let's just use a simpler approach for known patterns.
    return match.group(0)  # skip for now, handle manually

# Let's handle specific patterns we know exist:
# textBoxVoiceReplacePath.Text?.Trim() ?? ""
content = content.replace('textBoxVoiceReplacePath.Text?.Trim() ?? ""', '(textBoxVoiceReplacePath.Text == null ? "" : textBoxVoiceReplacePath.Text.Trim())')
content = content.replace('textBoxMergeOutput.Text?.Trim() ?? ""', '(textBoxMergeOutput.Text == null ? "" : textBoxMergeOutput.Text.Trim())')

# button?.Tag?.ToString()?.ToLower() ?? ".gif"
content = content.replace('button?.Tag?.ToString()?.ToLower() ?? ".gif"', '(button == null || button.Tag == null ? ".gif" : button.Tag.ToString().ToLower())')

# button?.Tag?.ToString()
content = content.replace('button?.Tag?.ToString()', '(button == null ? null : (button.Tag == null ? null : button.Tag.ToString()))')

# mi.CommandParameter as BatchFileItem; then item != null check - this is fine

# 2. Replace pattern matching: if (sender is Button button)
# -> Button button = sender as Button; if (button != null)
content = content.replace('if (sender is Button button)', 'Button button = sender as Button; if (button != null)')
content = content.replace('if (sender is MenuItem mi)', 'MenuItem mi = sender as MenuItem; if (mi != null)')

with open(r'd:\Tool\视频处理\VideoTools-1.4.0\VideoTools-1.4.0\VideoTools\MainWindow.xaml.cs', 'w', encoding='utf-8') as f:
    f.write(content)

print('Done CS5 fixes')
