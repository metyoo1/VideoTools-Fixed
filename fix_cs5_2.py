import re

with open(r'd:\Tool\视频处理\VideoTools-1.4.0\VideoTools-1.4.0\VideoTools\MainWindow.xaml.cs', 'r', encoding='utf-8') as f:
    content = f.read()

# 1. Replace ?. null conditional operators
content = content.replace('e.ErrorException?.HResult ?? 0', '(e.ErrorException == null ? 0 : e.ErrorException.HResult)')
content = content.replace('e.ErrorException?.Message ?? "未知错误"', '(e.ErrorException == null ? "未知错误" : e.ErrorException.Message)')

# 2. Replace pattern matching in foreach: if (child is FrameworkElement element && element != panel)
content = content.replace('if (child is FrameworkElement element && element != panel)',
                          'FrameworkElement element = child as FrameworkElement; if (element != null && element != panel)')

# 3. Replace out var in TryParse: out double value -> out value (declare before)
# Find: if (double.TryParse(textBoxMultiple.Text, out double value) && value > 0)
content = content.replace('if (double.TryParse(textBoxMultiple.Text, out double value) && value > 0)',
                          'double value; if (double.TryParse(textBoxMultiple.Text, out value) && value > 0)')

# 4. Replace button?.Tag?.ToString() patterns
content = content.replace('var tag = button.Tag?.ToString();', 'var tag = (button == null || button.Tag == null ? null : button.Tag.ToString());')

with open(r'd:\Tool\视频处理\VideoTools-1.4.0\VideoTools-1.4.0\VideoTools\MainWindow.xaml.cs', 'w', encoding='utf-8') as f:
    f.write(content)

print('Done CS5 fixes 2')
