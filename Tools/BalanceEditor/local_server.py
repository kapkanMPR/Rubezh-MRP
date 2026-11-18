#!/usr/bin/env python3
"""Rubezh Balance Editor v2.1 — offline browser UI + loopback-only bridge."""
from http.server import ThreadingHTTPServer, BaseHTTPRequestHandler
from urllib.parse import urlparse
from pathlib import Path
import json, threading, webbrowser
from project_sync import read_current, commit, save_named

BASE=Path(__file__).resolve().parent
LOCK=threading.RLock()
TEMPLATES=json.loads((BASE/'weapon-config.json').read_text(encoding='utf-8'))

class Handler(BaseHTTPRequestHandler):
 def _send(self,code,obj):
  raw=json.dumps(obj,ensure_ascii=False).encode('utf-8')
  self.send_response(code)
  self.send_header('Content-Type','application/json; charset=utf-8')
  self.send_header('Cache-Control','no-store')
  self.send_header('Content-Length',str(len(raw)))
  self.end_headers();self.wfile.write(raw)
 def do_GET(self):
  if urlparse(self.path).path=='/':
   raw=(BASE/'editor-v2.html').read_bytes()
   self.send_response(200);self.send_header('Content-Type','text/html; charset=utf-8')
   self.send_header('Cache-Control','no-store');self.send_header('Content-Length',str(len(raw)))
   self.end_headers();self.wfile.write(raw)
  elif urlparse(self.path).path=='/api/default':self._send(200,TEMPLATES)
  else:self._send(404,{'ok':False,'error':'Не найдено'})
 def do_POST(self):
  if urlparse(self.path).path not in ('/api/load','/api/apply','/api/save-profile'):
   return self._send(404,{'ok':False,'error':'Не найдено'})
  # JSON POST from the editor origin only: other web pages must not write local files.
  origin=self.headers.get('Origin','')
  if origin and origin not in ('http://127.0.0.1:8765','http://localhost:8765'):
   return self._send(403,{'ok':False,'error':'Запрещён посторонний источник запроса'})
  try:
   n=int(self.headers.get('Content-Length','0'))
   if n<2 or n>800000:raise ValueError('Неверный размер запроса')
   data=json.loads(self.rfile.read(n))
   project=data['project']
   with LOCK:
    endpoint=urlparse(self.path).path
    if endpoint=='/api/load':result=read_current(project,TEMPLATES)
    elif endpoint=='/api/apply':result=commit(project,data.get('revision'),data['weapons'],data['balance'],TEMPLATES)
    else:result=save_named(project,data['name'],data['weapons'],data['balance'],TEMPLATES)
   self._send(200,result)
  except (ValueError,KeyError,TypeError,OSError,json.JSONDecodeError) as exc:
   self._send(400,{'ok':False,'error':str(exc)})
 def log_message(self,format,*args):pass

if __name__=='__main__':
 server=ThreadingHTTPServer(('127.0.0.1',8765),Handler)
 print('Рубеж · Баланс-редактор v2.1: http://127.0.0.1:8765')
 threading.Timer(1,lambda:webbrowser.open('http://127.0.0.1:8765')).start()
 try:server.serve_forever()
 except KeyboardInterrupt:pass
