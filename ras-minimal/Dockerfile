FROM almalinux:9-minimal

RUN microdnf install -y libstdc++ shadow-utils \
    && microdnf clean all \
    && groupadd --gid 1000 grp1cv8 \
    && useradd --uid 1000 --gid 1000 --no-log-init --create-home --home-dir /home/usr1cv8 usr1cv8

COPY bin/ /opt/1cv8/x86_64/8.3.25.1520/
COPY conf/ /opt/1cv8/conf/
COPY docker-entrypoint.sh /usr/local/bin/docker-entrypoint.sh
RUN chmod +x /usr/local/bin/docker-entrypoint.sh

ENV RAS_PORT=1545

EXPOSE 1545 1555

USER usr1cv8

ENTRYPOINT ["/usr/local/bin/docker-entrypoint.sh"]