import re

with open(r'd:\Tool\视频处理\VideoTools-1.4.0\VideoTools-1.4.0\VideoTools\MainWindow.xaml.cs', 'r', encoding='utf-8') as f:
    content = f.read()

# Replace remaining simple string interpolations (no variables inside)
# These are just $"literal_string" -> "literal_string"
content = re.sub(r'\$"([^"\\]|\\.)*"', lambda m: '"' + m.group(0)[2:-1] + '"', content)

with open(r'd:\Tool\视频处理\VideoTools-1.4.0\VideoTools-1.4.0\VideoTools\MainWindow.xaml.cs', 'w', encoding='utf-8') as f:
    f.write(content)

print('Done2')
