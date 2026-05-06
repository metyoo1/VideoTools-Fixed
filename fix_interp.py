import re

with open(r'd:\Tool\视频处理\VideoTools-1.4.0\VideoTools-1.4.0\VideoTools\MainWindow.xaml.cs', 'r', encoding='utf-8') as f:
    content = f.read()

def replace_interpolation(match):
    s = match.group(0)
    inner = s[2:-1]

    # Find all {expr} or {expr:format} patterns
    parts = []
    last_end = 0
    vars_found = []

    # Match {expr} or {expr:format}
    for m in re.finditer(r'\{([^}]+(?:\:[^}]+)?)\}', inner):
        parts.append(inner[last_end:m.start()])
        expr = m.group(1)
        vars_found.append(expr.split(':')[0].strip())
        if ':' in expr:
            fmt = expr[expr.index(':'):]
            parts.append('{' + str(len(vars_found)-1) + fmt + '}')
        else:
            parts.append('{' + str(len(vars_found)-1) + '}')
        last_end = m.end()
    parts.append(inner[last_end:])

    if not vars_found:
        return s

    new_inner = ''.join(parts)
    args = ', '.join(vars_found)
    return 'string.Format("' + new_inner + '", ' + args + ')'

# Match $"..." (non-greedy, handle escaped quotes)
content = re.sub(r'\$"(?:[^"\\]|\\.)*"', replace_interpolation, content)

with open(r'd:\Tool\视频处理\VideoTools-1.4.0\VideoTools-1.4.0\VideoTools\MainWindow.xaml.cs', 'w', encoding='utf-8') as f:
    f.write(content)

print('Done')
