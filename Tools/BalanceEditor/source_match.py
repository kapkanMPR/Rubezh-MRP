"""Whitespace-independent matching for the editor's literal + numeric-hole schema.
Not a general regex rewriter: unsupported regex context fails closed. Numeric
capture spans remain offsets into the original source, so edits preserve trivia.
"""
import functools,re
NUMBER=r'(?P<num>-?(?:\d+(?:\.\d+)?|\.\d+))(?P<suffix>f?)'
HOLE='__RUBEZH_NUMERIC_HOLE_089__'
LEX=re.compile(r'@"(?:""|[^"])*"|"(?:\\.|[^"\\])*"|\'(?:\\.|[^\'\\])*\'|[A-Za-z_][A-Za-z_0-9]*|(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?[fFdDmMuUlL]*|=>|==|!=|<=|>=|&&|\|\||\+\+|--|\+=|-=|\*=|/=|\?\?|\?\.|<<|>>|[^\s]',re.S)
COMMENTS_LITERALS=re.compile(r'//[^\r\n]*|/\*.*?\*/|@"(?:""|[^"])*"|"(?:\\.|[^"\\])*"|\'(?:\\.|[^\'\\])*\'',re.S)

def literal_context(pattern):
 r"""Decode re.escape literals only, plus the one existing explicit \s* anchor."""
 out=[];i=0
 while i<len(pattern):
  if pattern.startswith(r'\s*',i):out.append(' ');i+=3;continue
  c=pattern[i]
  if c=='\\':
   i+=1
   if i==len(pattern) or pattern[i].isalnum():raise ValueError('Unsupported regex escape in balance context')
   out.append(pattern[i]);i+=1;continue
  if c in '.^$*+?{}[]\\|()':raise ValueError('Unsupported regex operator in balance context')
  out.append(c);i+=1
 return ''.join(out)

def _pattern_tokens(snippet):
 # Adjacent f suffix belongs to the numeric hole, not a new identifier.
 snippet=snippet.replace(HOLE+'f',HOLE+' f')
 return LEX.findall(snippet)

@functools.lru_cache(maxsize=256)
def compile_field(pattern):
 if pattern.count(NUMBER)!=1:raise ValueError('Unsupported numeric balance schema')
 prefix,suffix=pattern.split(NUMBER)
 prefix=literal_context(prefix);suffix=literal_context(suffix)
 tokens=_pattern_tokens(prefix+HOLE+suffix)
 pieces=[]
 for i,token in enumerate(tokens):
  if i:
   # Never allow a literal suffix to detach from its number.
   pieces.append('' if tokens[i-1]==HOLE and token=='f' and suffix.startswith('f') else r'\s+' if tokens[i-1][-1].isalnum() and token[0].isalnum() else r'\s*')
  pieces.append(NUMBER if token==HOLE else re.escape(token))
 if HOLE not in tokens:raise ValueError('Numeric hole merged into identifier')
 return re.compile(r'(?<![\w])'+''.join(pieces)+r'(?![\w])')

def masked_source(text):
 def mask(m):
  # Comments are legal trivia; strings/chars are barriers, not editable code.
  return ''.join('\n' if c=='\n' else ' ' for c in m.group()) if m.group().startswith(('/',)) else '\0'*len(m.group())
 return COMMENTS_LITERALS.sub(mask,text)

def field_matches(pattern,text):return list(compile_field(pattern).finditer(masked_source(text)))

@functools.lru_cache(maxsize=128)
def compile_literal(snippet):
 tokens=LEX.findall(snippet)
 if not tokens:raise ValueError('Empty source anchor')
 parts=[]
 for i,t in enumerate(tokens):
  if i:parts.append(r'\s+' if tokens[i-1][-1].isalnum() and t[0].isalnum() else r'\s*')
  parts.append(re.escape(t))
 return re.compile(r'(?<![\w])'+''.join(parts)+r'(?![\w])')

def literal_matches(snippet,text):return list(compile_literal(snippet).finditer(masked_source(text)))

def replace_literal(text,old,new):
 hits=literal_matches(old,text)
 if len(hits)!=1:raise ValueError('Source anchor must occur exactly once: '+old[:80])
 m=hits[0];return text[:m.start()]+new+text[m.end():]
